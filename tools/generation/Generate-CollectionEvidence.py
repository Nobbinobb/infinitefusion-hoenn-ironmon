"""Build reviewed native return-branch coordinates; never infer reasons from messages."""
from pathlib import Path
import argparse
import hashlib
import json
import re


def ruby(value):
    """Encode fixed registry data as Ruby literals without evaluating source text."""
    if value is None:
        return "nil"
    if isinstance(value, bool):
        return str(value).lower()
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False).replace("#{", "\\#{")
    if isinstance(value, dict):
        return "{ " + ", ".join(ruby(k) + " => " + ruby(v) for k, v in value.items()) + " }"
    if isinstance(value, list):
        return "[" + ", ".join(map(ruby, value)) + "]"
    return str(value)


def read_source(path):
    """Use the same LF source identity for archived and Windows checkout scripts."""
    return path.read_bytes().replace(b"\r\n", b"\n")


def definition(root, file, owner, method, kind, reasons, *, accuracy=False, locals=False):
    """Require an exact reviewed branch count and retain the source digest."""
    source = read_source(root / file)
    lines = source.decode("utf-8-sig").splitlines()
    current = None
    candidates = []
    for i, line in enumerate(lines):
        match = re.match(r"(?:class|module) (\w+)", line)
        if match:
            current = match[1]
        if current == owner and re.match(r"  def " + re.escape(method) + r"(?:\(|;|\s|$)", line):
            candidates.append(i)
    start = candidates[0]
    end = start if re.search(r"\bend\s*$", lines[start]) else next(i for i in range(start + 1, len(lines)) if re.match(r"  end\s*$", lines[i]))
    branches = []
    for i in range(start, end + 1):
        match = re.search(r"\breturn (true|false)\b", lines[i])
        if match:
            branches.append({"line": i + 1, "value": match[1]})
    if len(reasons) != len(branches):
        raise ValueError(f"{method}: expected {len(reasons)} branches, found {len(branches)}: " + repr([(b['line'], lines[b['line']-1].strip()) for b in branches]))
    for branch, reason in zip(branches, reasons):
        branch["reason"] = reason
    return {"path": file, "owner": owner, "method": method, "start_line": start + 1, "kind": kind, "sha256": hashlib.sha256(source).hexdigest(), "branches": branches, "accuracy": accuracy, "locals": locals}


def main():
    """Regenerate only against the reviewed game's branch structure."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--audit-output", type=Path)
    args = parser.parse_args()
    root = args.game_root / "Data/Scripts"
    checks = "011_Battle/001_Battler/009_Battler_UseMove_SuccessChecks.rb"
    statuses = "011_Battle/001_Battler/004_Battler_Statuses.rb"
    rows = [
        definition(root, checks, "PokeBattle_Battler", "pbCanChooseMove?", "move_gate", [
            "volatile:Disable", "volatile:HealBlock", "field:Gravity", "volatile:ThroatChop", "item:actual", "volatile:Taunt", "volatile:Torment", "volatile:Imprison", "item:ASSAULTVEST", "move:choice_restriction", "rule:allowed"]),
        definition(root, checks, "PokeBattle_Battler", "pbTryUseMove", "move_gate", [
            "rule:move_unavailable", "volatile:SkyDrop", "volatile:HyperBeam", "rule:battle_palace", "rule:skip_checks", "status:SLEEP", "status:FROZEN", "rule:caught_off_guard", "rule:disobedience", "ability:TRUANT", "volatile:Flinch", "volatile:Confusion", "status:PARALYSIS", "volatile:Attract", "rule:allowed"]),
        definition(root, checks, "PokeBattle_Battler", "pbSuccessCheckAgainstTarget", "target_gate", [
            "rule:charging", "move:specific_failure", "terrain:Psychic", "protection:CraftyShield", "protection:WideGuard", "protection:QuickGuard", "protection:Protect", "protection:KingsShield", "protection:SpikyShield", "protection:BanefulBunker", "protection:MatBlock", "reflection:MagicCoat", "reflection:MAGICBOUNCE", "ability:actual", "type:immunity", "type:prankster_dark", "ability:LEVITATE", "item:AIRBALLOON", "airborne:MagnetRise", "airborne:Telekinesis", "type:powder_grass", "ability:OVERCOAT", "item:SAFETYGOGGLES", "substitute:Substitute", "rule:allowed"]),
        definition(root, checks, "PokeBattle_Battler", "pbSuccessCheckPerHit", "per_hit", [
            "rule:charging", "volatile:LockOn", "move:override", "rule:called_move", "rule:accuracy_check", "rule:missed"]),
        definition(root, statuses, "PokeBattle_Battler", "pbCanInflictStatus?", "status", [
            "rule:fainted", "status:same_status", "status:existing_status", "substitute:Substitute", "weather:sunlight", "terrain:Electric", "terrain:Misty", "volatile:Uproar", "type:immunity", "ability:actual", "safeguard:Safeguard", "rule:allowed"]),
        definition(root, "011_Battle/002_Move/003_Move_Usage_Calculations.rb", "PokeBattle_Move", "pbAccuracyCheck", "accuracy", [
            "volatile:Telekinesis", "volatile:Minimize", "move:always_hit", "modifier:always_hit"], accuracy=True),
        definition(root, "011_Battle/003_Battle/001_PokeBattle_BattleCommon.rb", "PokeBattle_BattleCommon", "pbCaptureCalc", "capture", [], locals=True),
    ]
    rows[-1]["install_as"] = "ironmon_original_pb_capture_calc"
    rows.extend([
        definition(root, "011_Battle/001_Battler/005_Battler_StatStages.rb", "PokeBattle_Battler", "pbCanRaiseStatStage?", "stat_gate", ["rule:fainted", "stat:maximum", "rule:allowed"]),
        definition(root, "011_Battle/001_Battler/005_Battler_StatStages.rb", "PokeBattle_Battler", "pbCanLowerStatStage?", "stat_gate", ["rule:fainted", "substitute:Substitute", "field:Mist", "ability:actual", "ability:actual", "ability:ally", "stat:minimum", "rule:allowed"]),
    ])
    switching = "011_Battle/003_Battle/006_Battle_Action_Switching.rb"
    row = definition(root, switching, "PokeBattle_Battle", "pbOnActiveOne", "entry", ["rule:empty", "rule:entry_fainted", "rule:allowed"])
    source_lines = (root / switching).read_text(encoding="utf-8-sig").splitlines()
    row["edits"] = []
    for marker, replacement in [
        ("# Healing Wish", 'Ironmon::GameplayCollection::NativeEvidence.position_resolution("HealingWish",battler) do; # Healing Wish'),
        ("# Lunar Dance", 'end; Ironmon::GameplayCollection::NativeEvidence.position_resolution("LunarDance",battler) do; # Lunar Dance'),
        ("# Entry hazards", 'end; # Entry hazards'),
        ("# Stealth Rock", 'Ironmon::GameplayCollection::NativeEvidence.hazard("StealthRock",battler) do; # Stealth Rock'),
        ("# Spikes", 'end; Ironmon::GameplayCollection::NativeEvidence.hazard("Spikes",battler) do; # Spikes'),
        ("# Toxic Spikes", 'end; Ironmon::GameplayCollection::NativeEvidence.hazard("ToxicSpikes",battler) do; # Toxic Spikes'),
        ("# Sticky Web", 'end; Ironmon::GameplayCollection::NativeEvidence.hazard("StickyWeb",battler) do; # Sticky Web'),
        ("# Battler faints if", 'end; # Battler faints if'),
    ]:
        index = next(i for i in range(row["start_line"]-1, len(source_lines)) if source_lines[i].strip().startswith(marker))
        row["edits"].append({"line": index+1, "needle": marker, "replacement": replacement})
    rows.append(row)
    def edit_method(file, owner, method, edits):
        row = definition(root, file, owner, method, "source_observation", [])
        lines = (root / file).read_text(encoding="utf-8-sig").splitlines()
        row["edits"] = []
        for needle, replacement in edits:
            matches = [i for i in range(row["start_line"]-1, len(lines)) if needle in lines[i]]
            if len(matches) != 1:
                raise ValueError(f"Ambiguous observation {file}:{needle}")
            row["edits"].append({"line": matches[0]+1, "needle": needle, "replacement": replacement})
        rows.append(row)
    edit_method("011_Battle/001_Battler/008_Battler_UseMove_Targeting.rb", "PokeBattle_Battler", "pbChangeTargets", [
        ('PBDebug.log("[Move target changed] #{newTarget.pbThis}\'s Spotlight', 'Ironmon::GameplayCollection::NativeEvidence.redirect("Spotlight",newTarget); PBDebug.log("[Move target changed] #{newTarget.pbThis}\'s Spotlight'),
        ('PBDebug.log("[Move target changed] #{newTarget.pbThis}\'s Follow Me', 'Ironmon::GameplayCollection::NativeEvidence.redirect(newTarget.effects[PBEffects::RagePowder] ? "RagePowder" : "FollowMe",newTarget); PBDebug.log("[Move target changed] #{newTarget.pbThis}\'s Follow Me'),
    ])
    edit_method("011_Battle/003_Battle/003_Battle_StartAndEnd.rb", "PokeBattle_Battle", "pbGainMoney", [
        ("pbPlayer.money += tMoney", 'Ironmon::GameplayCollection::NativeEvidence.wallet("trainer_prize",self) { pbPlayer.money += tMoney }'),
        ("pbPlayer.money += @field.effects[PBEffects::PayDay]", 'Ironmon::GameplayCollection::NativeEvidence.wallet("money_move_reward",self) { pbPlayer.money += @field.effects[PBEffects::PayDay] }'),
    ])
    edit_method("011_Battle/003_Battle/003_Battle_StartAndEnd.rb", "PokeBattle_Battle", "pbLoseMoney", [
        ("pbPlayer.money -= tMoney", 'Ironmon::GameplayCollection::NativeEvidence.wallet("blackout_loss",self) { pbPlayer.money -= tMoney }'),
    ])
    edit_method("011_Battle/001_Battler/008_Battler_UseMove_Targeting.rb", "PokeBattle_Battler", "pbChangeTargetByAbility", [
        ("targets.clear", "Ironmon::GameplayCollection::NativeEvidence.redirect(drawingAbility.to_s,b); targets.clear"),
    ])
    edit_method("011_Battle/003_Battle/001_PokeBattle_BattleCommon.rb", "PokeBattle_BattleCommon", "pbThrowPokeBall", [
        ('pbDisplay(_INTL("But there was no target..."))', 'Ironmon::GameplayCollection::NativeEvidence.capture_gate("no_target",battler); pbDisplay(_INTL("But there was no target..."))'),
        ('pbDisplay(_INTL("The Trainer blocked your Poké Ball!', 'Ironmon::GameplayCollection::NativeEvidence.capture_gate("trainer_blocked",battler); pbDisplay(_INTL("The Trainer blocked your Poké Ball!'),
        ('pbDisplay(_INTL("The Pokémon is impossible to catch!', 'Ironmon::GameplayCollection::NativeEvidence.capture_gate("catch_disabled",battler); pbDisplay(_INTL("The Pokémon is impossible to catch!'),
    ])
    perhit = next(row for row in rows if row["kind"] == "per_hit")
    lines = (root / perhit["path"]).read_text(encoding="utf-8-sig").splitlines()
    index = next(i for i,line in enumerate(lines) if "miss = true if rand(2)==1" in line)
    perhit["edits"] = [{"line": index+1, "needle": "rand(2)==1", "replacement": "Ironmon::GameplayCollection::NativeEvidence.mankey_miss(rand(2)==1,self)"}]
    row = definition(root, "011_Battle/001_Battler/006_Battler_AbilityAndItem.rb", "PokeBattle_Battler", "pbSymbiosis", "item_transfer", [])
    native = (root / row["path"]).read_text(encoding="utf-8-sig").splitlines()
    index = next(i for i in range(row["start_line"]-1, len(native)) if "self.item = b.item" in native[i])
    row["edits"] = [{"line": index+1, "needle": "self.item = b.item", "replacement": 'Ironmon::GameplayCollection::ItemIdentity.prepare_next(b,self,"symbiosis"); self.item = b.item'}]
    rows.append(row)
    for file, owner, reasons in [
        ("004_Move_Effects_Generic.rb", "PokeBattle_TwoTurnMove", ["move:charging"]),
        ("005_Move_Effects_000-07F.rb", "PokeBattle_Move_070", []),
        ("006_Move_Effects_080-0FF.rb", "PokeBattle_Move_088", ["rule:pursuit_switch"]),
        ("006_Move_Effects_080-0FF.rb", "PokeBattle_Move_0A5", ["move:always_hit"]),
        ("007_Move_Effects_100-17F.rb", "PokeBattle_Move_111", ["move:delayed_setup"]),
        *[("007_Move_Effects_100-17F.rb", "PokeBattle_Move_" + code, ["move:always_hit"]) for code in ("139", "13A", "13B", "13C", "147")],
    ]:
        row = definition(root, "011_Battle/002_Move/" + file, owner, "pbAccuracyCheck", "accuracy", reasons)
        row["ohko_accuracy"] = owner == "PokeBattle_Move_070"
        rows.append(row)
    output = Path(__file__).resolve().parents[2] / "src/collection/Native_Registry.rb"
    handlers = (root / "011_Battle/002_BattleHandlers.rb").read_text(encoding="utf-8-sig")
    parameters = {name: [part.strip().split("=", 1)[0] for part in signature.split(",")] for name, signature in re.findall(r"def self\.(trigger\w+)\(([^)]+)\)", handlers)}
    hp_sources = {}
    special = {"PokeBattle_Struggle": "recoil", "PokeBattle_RecoilMove": "recoil", "PokeBattle_Move_03A": "self_cost", "PokeBattle_Move_05A": "normalization", "PokeBattle_Move_074": "move_hit", "PokeBattle_Move_10C": "self_cost", "PokeBattle_Move_10D": "self_cost", "PokeBattle_Move_160": "retaliation", "PokeBattle_Move_170": "self_cost"}
    for file in root.rglob("*.rb"):
        relative = file.relative_to(root).as_posix()
        if relative.startswith(("997", "998", "999")):
            continue
        raw = read_source(file)
        lines = raw.decode("utf-8-sig").splitlines()
        owner, method, handler = "", "", None
        found = {}
        for number, line in enumerate(lines, 1):
            match = re.match(r"(?:class|module) ([\w:]+)", line)
            if match:
                owner = match[1]
            match = re.match(r"\s*def (?:self\.)?(\w+[!?=]?)", line)
            if match:
                method = match[1]
            match = re.search(r"BattleHandlers::\w+\.add\(:(\w+)", line)
            if match:
                handler = match[1]
            if "def " in line or line.lstrip().startswith("#") or not re.search(r"\bpb(?:Reduce|Recover)HP\(|\.hp\s*[-+*/]?=(?!=)", line):
                continue
            kind = special.get(owner, "other")
            if method == "pbSelfKO":
                kind = "self_ko"
            elif method in ("pbCrashDamage", "pbRecoilDamage"):
                kind = "recoil"
            elif "EndOfRound" in file.name:
                kind = "residual"
            found[str(number)] = {"owner": owner, "method": method, "handler": handler, "loss_kind": kind}
        if found:
            if file.name in hp_sources:
                raise ValueError("Ambiguous HP source basename: " + file.name)
            hp_sources[file.name] = {"path": relative, "sha256": hashlib.sha256(raw).hexdigest(), "lines": found}
    observations = []
    abilities = "011_Battle/003_BattleHandlers_Abilities.rb"
    for registry, identifier, needle, call in [
        ("OnEndOfUsingMoveAbility", "MAGICIAN", "user.item = b.item", 'ItemIdentity.prepare_next(b,user,"steal")'),
        ("OnLosingHPAbility", "PICKPOCKET", "target.item = user.item", 'ItemIdentity.prepare_next(user,target,"steal")'),
        ("EORGainItemAbility", "HARVEST", "battler.item = battler.recycleItem", 'ItemIdentity.prepare_recovery(battler,battler,"harvest")'),
        ("EORGainItemAbility", "PICKUP", "battler.item = foundItem", 'ItemIdentity.prepare_recovery(fromBattler,battler,"pickup")'),
    ]:
        source = read_source(root / abilities)
        lines = source.decode("utf-8-sig").splitlines()
        start = next(i for i, line in enumerate(lines) if f".add(:{identifier}," in line)
        registry = re.search(r"BattleHandlers::(\w+)\.add", lines[start])[1]
        start += 1
        end = next(i for i in range(start, len(lines)) if lines[i].strip() == "}")
        index = next(i for i in range(start, end + 1) if needle in lines[i])
        observations.append({"path": abilities, "sha256": hashlib.sha256(source).hexdigest(), "registry": registry, "identifier": identifier,
                             "start_line": start + 1, "end_line": end + 1, "edits": [{"line": index + 1, "needle": needle, "replacement": "Ironmon::GameplayCollection::" + call + "; " + needle}]})
    handler_loss = {**dict.fromkeys(["AFTERMATH", "INNARDSOUT", "IRONBARBS", "JABOCABERRY", "ROCKYHELMET", "ROWAPBERRY"], "retaliation"),
                    **dict.fromkeys(["DRYSKIN", "SOLARPOWER", "BADDREAMS", "BLACKSLUDGE", "STICKYBARB"], "residual"), "LIFEORB": "recoil"}
    for file in hp_sources.values():
        for entry in file["lines"].values():
            if entry["handler"] in handler_loss:
                entry["loss_kind"] = handler_loss[entry["handler"]]
    output.write_text("# Generated by tools/generation/Generate-CollectionEvidence.py; reviewed native branches.\nmodule Ironmon\n  module GameplayCollection\n    module NativeEvidence\n      NATIVE_DEFINITIONS = [\n" + ",\n".join("        " + ruby(row) for row in rows) + "\n      ].freeze\n      HANDLER_PARAMETERS = " + ruby(parameters) + ".freeze\n      HP_CALLSITES = " + ruby(hp_sources) + ".freeze\n      SOURCE_OBSERVATIONS = " + ruby(observations) + ".freeze\n    end\n  end\nend\n", encoding="utf-8")
    if args.audit_output:
        args.audit_output.parent.mkdir(parents=True, exist_ok=True)
        args.audit_output.write_text(json.dumps({"definitions": rows, "handler_parameters": parameters, "hp_sources": hp_sources, "callback_observations": observations}, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    print(f"Generated {len(rows)} reviewed native definitions.")


if __name__ == "__main__":
    main()
