param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "data\move_power_presentation.json")
)

$ErrorActionPreference = "Stop"

$calculatedMoves = @(
    "RETURN", "FRUSTRATION", "ERUPTION", "WATERSPOUT", "CRUSHGRIP",
    "WRINGOUT", "GYROBALL", "POWERTRIP", "STOREDPOWER", "PUNISHMENT",
    "HIDDENPOWER", "HIDDENPOWER2", "NATURALGIFT", "TRUMPCARD", "REVERSAL",
    "FLAIL", "ELECTROBALL", "LOWKICK", "GRASSKNOT", "HEATCRASH",
    "HEAVYSLAM", "FLING", "SPITUP", "STEAMROLLER", "STOMP", "BULLDOZE",
    "SURF", "EARTHQUAKE", "GUST", "TWISTER", "FUSIONBOLT", "FUSIONFLARE",
    "VENOSHOCK", "SMELLINGSALT", "WAKEUPSLAP", "FACADE", "HEX", "BRINE",
    "REVENGE", "AVALANCHE", "ASSURANCE", "ROUND", "PAYBACK", "RETALIATE",
    "ACROBATICS", "WEATHERBALL", "PURSUIT", "FURYCUTTER", "ECHOEDVOICE",
    "SOLARBLADE", "SOLARBEAM", "WHIRLPOOL", "ICEBALL", "ROLLOUT",
    "GRASSPLEDGE", "FIREPLEDGE", "WATERPLEDGE", "FLYINGPRESS",
    "STOMPINGTANTRUM"
)
$randomOutcomeMoves = @("PRESENT", "MAGNITUDE", "PSYWAVE")
$structuredMultiHitMoves = @(
    "TRIPLEKICK", "WATERSHURIKEN", "BEATUP", "DOUBLEIRONBASH"
)
$directHpDamageMoves = @(
    "SONICBOOM", "DRAGONRAGE", "NATURESMADNESS", "SUPERFANG",
    "SEISMICTOSS", "NIGHTSHADE", "ENDEAVOR", "FISSURE", "SHEERCOLD",
    "GUILLOTINE", "HORNDRILL", "COUNTER", "MIRRORCOAT", "METALBURST",
    "BIDE", "FINALGAMBIT"
)
$randomMultiHitMoves = @(
    "PINMISSILE", "ARMTHRUST", "BULLETSEED", "BONERUSH", "ICICLESPEAR",
    "TAILSLAP", "SPIKECANNON", "COMETPUNCH", "FURYSWIPES", "BARRAGE",
    "DOUBLESLAP", "FURYATTACK", "ROCKBLAST"
)
$fixedMultiHitMoves = @(
    "DUALCHOP", "DOUBLEKICK", "BONEMERANG", "DOUBLEHIT", "GEARGRIND",
    "TWINEEDLE"
)
$moveIds = @(
    $calculatedMoves +
    $randomOutcomeMoves +
    $structuredMultiHitMoves +
    $directHpDamageMoves +
    $randomMultiHitMoves +
    $fixedMultiHitMoves
)
if ($moveIds.Count -ne 101 -or @($moveIds | Sort-Object -Unique).Count -ne 101) {
    throw "The move-power presentation rules must define 101 unique moves."
}
$conditionIndicatorMoves = @(
    "REVENGE", "AVALANCHE", "ASSURANCE", "ROUND", "PAYBACK", "RETALIATE",
    "PURSUIT", "GRASSPLEDGE", "FIREPLEDGE", "WATERPLEDGE"
)
$enemySensitiveMoves = @(
    "RETURN", "FRUSTRATION", "HIDDENPOWER", "HIDDENPOWER2", "NATURALGIFT",
    "FLING", "ACROBATICS", "BEATUP", "FINALGAMBIT"
)
$dynamicTypeMoves = @("HIDDENPOWER", "HIDDENPOWER2", "NATURALGIFT", "WEATHERBALL")
$offlineNativeMoves = @(
    "RETURN", "FRUSTRATION", "ERUPTION", "WATERSPOUT", "POWERTRIP",
    "STOREDPOWER", "HIDDENPOWER", "HIDDENPOWER2", "NATURALGIFT", "TRUMPCARD",
    "REVERSAL", "FLAIL", "FLING", "FACADE", "ACROBATICS"
)
$fixedDamageValues = @{
    "SONICBOOM" = 20
    "DRAGONRAGE" = 40
}
$levelDamageMoves = @("SEISMICTOSS", "NIGHTSHADE")
$hiddenFixedDamageMoves = @("NATURESMADNESS", "SUPERFANG", "ENDEAVOR")
$unknownDamageMoves = @(
    "GYROBALL", "ELECTROBALL", "COUNTER", "MIRRORCOAT", "METALBURST", "BIDE"
)
$oneHitKillMoves = @("FISSURE", "SHEERCOLD", "GUILLOTINE", "HORNDRILL")
function Get-PresentationMode {
    param([string]$MoveId)

    if ($conditionIndicatorMoves -contains $MoveId) { return "condition" }
    if ($fixedDamageValues.ContainsKey($MoveId)) { return "fixed_damage" }
    if ($levelDamageMoves -contains $MoveId) { return "level_damage" }
    if ($hiddenFixedDamageMoves -contains $MoveId) { return "hidden_fixed_damage" }
    if ($unknownDamageMoves -contains $MoveId) { return "unknown_damage" }
    if ($oneHitKillMoves -contains $MoveId) { return "one_hit_ko" }

    switch ($MoveId) {
        "PRESENT" { return "present" }
        "MAGNITUDE" { return "magnitude" }
        "PSYWAVE" { return "psywave" }
        "TRIPLEKICK" { return "triple_kick" }
        "WATERSHURIKEN" { return "water_shuriken" }
        "BEATUP" { return "beat_up" }
        "DOUBLEIRONBASH" { return "double_iron_bash" }
        "FINALGAMBIT" { return "current_hp_damage" }
    }
    if ($randomMultiHitMoves -contains $MoveId) { return "random_multi_hit" }
    if ($fixedMultiHitMoves -contains $MoveId) { return "fixed_multi_hit" }
    if ($calculatedMoves -contains $MoveId) { return "calculated" }
    throw "No move-power presentation mode is defined for $MoveId."
}

$moves = foreach ($moveId in $moveIds) {
    $move = [ordered]@{
        id = $moveId
        mode = Get-PresentationMode $moveId
    }
    if ($dynamicTypeMoves -contains $moveId) {
        $move.dynamic_type = $true
    }
    if ($enemySensitiveMoves -contains $moveId) {
        $move.enemy_sensitive = $true
    }
    if ($offlineNativeMoves -contains $moveId) {
        $move.offline_native = $true
    }
    if ($fixedDamageValues.ContainsKey($moveId)) {
        $move.fixed_value = $fixedDamageValues[$moveId]
    }
    if ($moveId -eq "WATERSHURIKEN") {
        $move.fixed_form_species = "GRENINJA"
        $move.fixed_form = 2
        $move.fixed_form_power = 20
        $move.fixed_form_hits = 3
    }
    switch ($moveId) {
        "TRUMPCARD" {
            $move.decrement_pp_before_power = $true
        }
        "FUSIONBOLT" {
            $move.prior_field_effect = "FusionFlare"
        }
        "FUSIONFLARE" {
            $move.prior_field_effect = "FusionBolt"
        }
        "FURYCUTTER" {
            $move.prospective_counter_scope = "user"
            $move.prospective_counter_effect = "FuryCutter"
            $move.prospective_counter_growth = "doubling"
            $move.prospective_counter_maximum_power = 160
        }
        "ECHOEDVOICE" {
            $move.prospective_counter_scope = "side"
            $move.prospective_counter_effect = "EchoedVoiceCounter"
            $move.prospective_counter_used_effect = "EchoedVoiceUsed"
            $move.prospective_counter_growth = "linear"
            $move.prospective_counter_maximum = 5
        }
    }
    [pscustomobject]$move
}
$document = [ordered]@{
    version = 1
    moves = @($moves)
}
$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$document | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "Generated move-power presentation catalog with $($moves.Count) moves."
