# HM replacement tools

Ironmon does not require a spare party Pokemon for field moves. While the mode
is active, HM rewards are replaced before item randomization by the permanent
field tool that performs the same overworld action.

| HM move | Replacement tool |
| --- | --- |
| Cut | Machete |
| Fly | Teleporter |
| Surf | Surfboard |
| Strength | Lever |
| Waterfall | Jetpack |
| Dive | Scuba Gear |
| Teleport | Teleporter |
| Flash | Lantern |
| Rock Smash | Pickaxe |
| Rock Climb | Climbing Gear |

Both items found on the map and items received from events use this conversion.
The replacement bypasses item randomization, ensuring that story progression
cannot lose the required tool. If the tool is already owned, the reward is
treated as collected without adding a duplicate key item.

When an existing Ironmon save is loaded, every owned HM is exchanged for its
tool. The conversion is transactional: it works on a copy of the Bag and only
replaces the live Bag after every required tool has been stored and every HM has
been removed successfully. HM02 (Fly) and HM07 (Teleport) both map to the same
Teleporter.
