# Ironmon 0.3.1

This patch refines the Milestone 3 Step 3.1 ability eligibility rules.

- Wonder Guard, Stance Change, Multitype, and Flower Gift are available in the
  universal random pool.
- An active Wonder Guard holder has 1 HP; future base-stat randomization remains
  independent of the selected ability slot.
- Forecast, Zen Mode, Schooling, Power Construct, Battle Bond, RKS System, and
  Ice Face are random candidates only for their supported exact species.
- Disguise and Shields Down are random candidates for Mimikyu and Minior and can
  pass to fusions containing the corresponding component.
- Exact-species abilities cannot leak into a fusion through component
  inheritance; an ineligible inherited slot receives a deterministic universal
  replacement.
- Randomized Multitype and Shields Down now use the generated active ability in
  the base game's out-of-battle checks.
- Ability generator schema and eligibility fingerprint versions are increased
  so incompatible rules cannot silently reinterpret an active run.

Because this changes deterministic assignments, development saves created with
the earlier schema must start a fresh run (F7) rather than being reinterpreted.

The release retains all Ironmon 0.3.0 functionality and targets Pokemon
Infinite Fusion 2 version 6.8.0.
