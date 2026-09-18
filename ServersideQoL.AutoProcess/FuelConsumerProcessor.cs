using ServersideQoL.Processors;
using ServersideQoL.Utilities;
using UnityEngine;

namespace ServersideQoL.AutoProcess;

[Processor("2374d946-4551-45f5-91db-a4ccf06d7ab3")]
[RunAfter<ContainerRegistryProcessor>]
public sealed class FuelConsumerProcessor : Processor<FuelConsumerProcessor.PrefabInfo>
{
  public sealed record PrefabInfo(CookingStation? CookingStation, Fireplace? Fireplace) : ProcessorPrefabInfo
  {
    public override bool IsValid => CookingStation is { m_useFuel: true, m_fuelItem: not null } || Fireplace is { m_fuelItem: not null };
  }

  SectorDictionary<HashSet<ServersideQoLZDO>>? _stations;
  SectorDictionary<SharedItemDataKey, HashSet<ServersideQoLZDO>>? _containersByItemName;

  protected override void Initialize()
  {
    Instance<ContainerRegistryProcessor>().ContainerChanged -= OnContainerChanged;
    if (Config.Instance.FeedFromContainers.Value && (Config.Instance.FeedOvens.Value || Config.Instance.FeedFireSources.Value))
    {
      _stations = new(Mathf.Max(Config.Instance.FeedFromContainersRange.Value, Config.Instance.FeedFromContainersMaxRange.Value));
      _containersByItemName = Instance<ContainerRegistryProcessor>().GetContainersByItemName(_stations.SectorWidth);
      Instance<ContainerRegistryProcessor>().ContainerChanged += OnContainerChanged;
    }
    else
    {
      _stations = null;
      _containersByItemName = null;
    }
  }

  protected override ProcessResult Process(ServersideQoLZDO zdo, IReadOnlyList<Peer> peers, PrefabInfo prefabInfo)
  {
    if (zdo.Vars.GetCreator().Value is 0)
      return ProcessResult.UnregisterProcessor;

    if (_stations is null || _containersByItemName is null)
      return ProcessResult.UnregisterProcessor;

    if (!(prefabInfo.CookingStation is not null ? Config.Instance.FeedOvens.Value : Config.Instance.FeedFireSources.Value))
      return ProcessResult.UnregisterProcessor;

    _stations.TryAdd(zdo);

    if (!CheckMinDistance(peers, zdo, Config.Instance.FeedFromContainersMinPlayerDistance.Value))
      return ScheduleReprocessing();

    float maxFuel;
    if (prefabInfo.CookingStation is not null)
      maxFuel = zdo.Fields<CookingStation>().GetInt(static () => x => x.m_maxFuel);
    else
    {
      var fields = zdo.Fields<Fireplace>();
      if (fields.GetBool(static () => x => x.m_infiniteFuel))
        return default;
      if (!fields.GetBool(static () => x => x.m_canRefill))
        return default;
      maxFuel = fields.GetFloat(static () => x => x.m_maxFuel);
    }

    /// <see cref="CookingStation.RPC_AddFuel"/> <see cref="Fireplace.RPC_AddFuel"/>
    var currentFuel = zdo.Vars.GetFuel();
    var maxFuelAdd = (int)(maxFuel - currentFuel);
    if (maxFuelAdd <= maxFuel / 2)
      return default;

    var result = ProcessResult.Default;
    var fuelItem = (prefabInfo.CookingStation?.m_fuelItem ?? prefabInfo.Fireplace!.m_fuelItem).m_itemData;
    var addedFuel = 0;
    List<ServersideQoLZDO>? toRemove = null;
    List<ItemDrop.ItemData>? removeSlots = null;

    foreach (var containers in _containersByItemName.EnumerateAdjacent((zdo.ZDO.GetPosition(), fuelItem.m_shared)))
    {
      toRemove?.Clear();
      foreach (var containerZdo in containers)
      {
        if (Instance<ContainerRegistryProcessor>().GetState(containerZdo) is not { } containerState)
        {
          (toRemove ??= []).Add(containerZdo);
          continue;
        }

        if (containerZdo.Vars.GetInUse()) // || !CheckMinDistance(peers, containerZdo))
          continue; // in use or player to close

        var feedRangeSqr = containerState.FeedRange ?? Config.Instance.FeedFromContainersRange.Value;
        feedRangeSqr *= feedRangeSqr;
        if (feedRangeSqr is 0f || Utils.DistanceSqr(zdo.ZDO.GetPosition(), containerZdo.ZDO.GetPosition()) > feedRangeSqr)
          continue;

        var inventory = containerState.GetInventory();
        removeSlots?.Clear();
        var addFuel = 0;
        var leave = Config.Instance.FeedFromContainersLeaveAtLeastFuel.Value;
        var found = false;
        var requestOwn = false;
        foreach (var slot in inventory.Items.Where(x => new ItemDataKey(x) == fuelItem).OrderBy(static x => x.m_stack))
        {
          found = found || slot is { m_stack: > 0 };
          var take = Math.Min(maxFuelAdd, slot.m_stack);
          var leaveDiff = Math.Min(take, leave);
          leave -= leaveDiff;
          take -= leaveDiff;
          if (take is 0)
            continue;
          else if (!containerZdo.IsOwnerOrUnassigned())
          {
            requestOwn = true;
            break;
          }

          addFuel += take;
          slot.m_stack -= take;
          if (slot.m_stack is 0)
            (removeSlots ??= []).Add(slot);

          maxFuelAdd -= take;
          if (maxFuelAdd is 0)
            break;
        }

        if (requestOwn)
        {
          result |= ScheduleReprocessing(Instance<ContainerRegistryProcessor>().RequestOwnership(containerZdo, default));
          continue;
        }

        if (addFuel is 0)
        {
          if (!found)
            (toRemove ??= []).Add(containerZdo);
          continue;
        }

        if (removeSlots is { Count: > 0 })
        {
          foreach (var remove in removeSlots)
            inventory.Items.Remove(remove);

          if (inventory.Items is { Count: 0 })
            (toRemove ??= []).Add(containerZdo);
        }

        zdo.ReleaseOwnership();
        currentFuel += addFuel;
        zdo.Vars.SetFuel(currentFuel);
        inventory.Save();

        addedFuel += addFuel;

        if (maxFuelAdd is 0)
          break;
      }

      if (toRemove is not null)
      {
        foreach (var containerZdo in toRemove)
          containers.Remove(containerZdo);
      }

      if (maxFuelAdd is 0)
        break;
    }

    if (addedFuel is not 0)
    {
      ShowMessage(peers, zdo,
          Config.Instance.Localization.Value.FormatFuelAdded(prefabInfo.CookingStation?.m_name ?? prefabInfo.Fireplace!.m_name, fuelItem.m_shared.m_name, addedFuel),
          Config.Instance.OreOrFuelAddedMessageType.Value);
    }

    return result;
  }

  void OnContainerChanged(ServersideQoLZDO containerZdo, ContainerState state)
  {
    if (_stations is null)
      throw new Exception("bug");

    var feedRangeSqr = state.FeedRange ?? Config.Instance.FeedFromContainersRange.Value;
    feedRangeSqr *= feedRangeSqr;
    if (feedRangeSqr is 0f)
      return;

    if (state.GetInventory() is not { Items.Count: > 0 } inventory)
      return;

    foreach (var stations in _stations.EnumerateAdjacent(containerZdo.ZDO.GetPosition()))
    {
      foreach (var zdo in stations)
      {
        if (Utils.DistanceSqr(zdo.ZDO.GetPosition(), containerZdo.ZDO.GetPosition()) <= feedRangeSqr)
          ScheduleReprocessing(zdo);
      }
    }
  }
}
