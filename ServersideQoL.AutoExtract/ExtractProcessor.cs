namespace ServersideQoL.AutoExtract;

[Processor("9d9305cb-0a0c-47b0-aa3d-edec9873a506")]
public sealed class ExtractProcessor : Processor<ExtractProcessor.PrefabInfo>
{
  public sealed record PrefabInfo(Beehive? Beehive, SapCollector? SapCollector) : ProcessorPrefabInfo;

  const float TapRetryDelay = 10f;
  const int MaxTapAttempts = 6;
  const float TapBackoffDelay = 300f;

  sealed class TapState
  {
    public int Attempts;
    public DateTimeOffset NextTap;
  }

  readonly Dictionary<ServersideQoLZDO, TapState> _tapStates = [];
  bool _warnedTapIgnored;

  protected override void Initialize()
  {
    _tapStates.Clear();
  }

  protected override ProcessResult Process(ServersideQoLZDO zdo, IReadOnlyList<Peer> peers, PrefabInfo prefabInfo)
  {
    if (zdo.Vars.GetCreator().Value is 0)
      return ProcessResult.UnregisterProcessor;

    if (!(prefabInfo.Beehive is not null ? Config.Instance.ExtractBeehives.Value : Config.Instance.ExtractSapCollectors.Value))
      return ProcessResult.UnregisterProcessor;

    if (!_tapStates.TryGetValue(zdo, out var state))
    {
      _tapStates.Add(zdo, state = new());
      zdo.Destroyed += x => _tapStates.Remove(x);
    }

    // GetLevel defaults to 1, so pass 0
    if (zdo.Vars.GetLevel(0) <= 0)
    {
      state.Attempts = 0;
      return default;
    }

    // Without AutoStore picking them up, the dropped items would just pile up on the ground.
    // The station writes its ZDO regularly, so it is processed again once AutoStore is enabled
    if (Config.Instance.RequireAutoStorePickup.Value && !Config.Instance.AutoStorePickupEnabled)
      return default;

    if (!CheckMinDistance(peers, zdo, Config.Instance.ExtractMinPlayerDistance.Value))
      return ScheduleReprocessing(); // player to close

    // Only the owning client can extract. The owner is a connected client which has the station loaded,
    // so the RPC never goes to ZRoutedRpc.Everybody (which would make every client drop the items)
    if (zdo.IsOwnerOrUnassigned())
      return ScheduleReprocessing(TapRetryDelay);

    var now = DateTimeOffset.UtcNow;
    if (now < state.NextTap)
      return ScheduleReprocessing((float)(state.NextTap - now).TotalSeconds); // wait for the owner to reset the level

    if (state.Attempts >= MaxTapAttempts)
    {
      if (!_warnedTapIgnored)
      {
        _warnedTapIgnored = true;
        Logger.LogWarning($"{prefabInfo.Beehive?.m_name ?? prefabInfo.SapCollector!.m_name} at {zdo.ZDO.GetPosition()} ignored {MaxTapAttempts} extract requests, retrying every {TapBackoffDelay}s. If this happens everywhere, a game update may have changed the extract RPC");
      }
      state.Attempts = 0;
      state.NextTap = now.AddSeconds(TapBackoffDelay);
      return ScheduleReprocessing(TapBackoffDelay);
    }

    // The owner drops the items at the spawn point like extracting by hand, resets the level
    // and thereby triggers reprocessing. AutoStore then puts the dropped items into containers
    state.Attempts++;
    state.NextTap = now.AddSeconds(TapRetryDelay);
    if (prefabInfo.Beehive is not null)
      zdo.RPC.Beehive.Extract();
    else
      zdo.RPC.SapCollector.Extract();
    return ScheduleReprocessing(TapRetryDelay);
  }
}
