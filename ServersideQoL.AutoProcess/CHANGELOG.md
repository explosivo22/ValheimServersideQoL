### v2.1.0
- Added option `FeedFromContainersRangeSignPrefix` [#270](https://github.com/ArgusMagnus/ValheimServersideQoL/issues/270) 
- Fixed an issue that caused range configuration with ContainerSigns to not work [#273](https://github.com/ArgusMagnus/ValheimServersideQoL/pull/273) (contributed by [Lempac](https://github.com/Lempac))
- Added option `FeedFromContainersExcludeOre` to exclude certain ore items (e.g. `FineWood` in kilns) from being fed to smelters from containers
- Added options `FeedOvens` and `FeedFireSources` to automatically feed ovens and fire sources (fire pits, hearths, braziers, torches, etc.) with fuel from nearby containers
- Added options `FeedFermenters` and `ExtractFermenters` to automatically add fermentable items to fermenters and move finished products (e.g. mead) into nearby containers. `FeedFromContainersLeaveAtLeastFermentable` (default 0) controls how many fermentable items are left in a container

### v2.0.11
- Moved `FeedFromContainersMaxRange` config option from ContainerSigns to AutoProcess
- Support for the new core options `UnifiedConfig` and `ConfigPerWorld`

### v2.0.0
- Initial release