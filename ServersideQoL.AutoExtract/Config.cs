using BepInEx.Configuration;

namespace ServersideQoL.AutoExtract;

public sealed class Config(ConfigFile cfg, Logger logger) : ConfigBase<Config>(cfg, logger)
{
  public override ConfigEntry<bool> Enabled { get; } = BindEx(cfg, true,
    "Enables/disables the entire mod");
  public ConfigEntry<bool> ExtractBeehives { get; } = BindEx(cfg, true,
    "True to automatically extract honey, feathers, etc. from beehives and bird nests. The items are dropped like when extracting by hand, use the AutoStore mod to put them into containers");
  public ConfigEntry<bool> ExtractSapCollectors { get; } = BindEx(cfg, true,
    "True to automatically extract sap from sap collectors. The items are dropped like when extracting by hand, use the AutoStore mod to put them into containers");
  public ConfigEntry<bool> RequireAutoStorePickup { get; } = BindEx(cfg, true, """
    True to only extract while the AutoStore mod's AutoPickup option is enabled, so the dropped items do not pile up on the ground.
    AutoStore only puts items into containers which already contain said item, so put the first honey, feathers and sap into a nearby container by hand.
    """);
  public ConfigEntry<float> ExtractMinPlayerDistance { get; } = BindEx(cfg, 4f,
    "Min distance all players must have to a beehive, bird nest or sap collector");

  public bool AutoStorePickupEnabled => Shared.AutoStorePickup?.Value is true;
}
