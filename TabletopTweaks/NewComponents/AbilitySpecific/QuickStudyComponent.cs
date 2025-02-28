﻿using HarmonyLib;
using JetBrains.Annotations;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.Root;
using Kingmaker.ElementsSystem;
using Kingmaker.UI.Common;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Kingmaker.UI.Tooltip;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using TabletopTweaks.Utilities;
using UnityEngine;

namespace TabletopTweaks.NewComponents.AbilitySpecific {
    [TypeId("4515aeab69cc419ba926987dd2cce54f")]
    class QuickStudyComponent : AbilityApplyEffect, IAbilityRestriction, IAbilityRequiredParameters {

        public AbilityParameter RequiredParameters {
            get {
                return AbilityParameter.SpellSlot;
            }
        }

        public override void Apply(AbilityExecutionContext context, TargetWrapper target) {
            if (context.Ability.ParamSpellSlot == null || context.Ability.ParamSpellSlot.Spell == null) {
                PFLog.Default.Error(context.AbilityBlueprint, string.Format("Target spell is missing: {0}", context.AbilityBlueprint), Array.Empty<object>());
                return;
            }
            if (context.Ability.ParamSpellSlot.Spell.Spellbook == null) {
                PFLog.Default.Error(context.AbilityBlueprint, string.Format("Spellbook is missing: {0}", context.AbilityBlueprint), Array.Empty<object>());
                return;
            }
            var unitBooks = ValidSpellbooks(context.MaybeCaster);
            if (!unitBooks.Contains(context.Ability.ParamSpellSlot.Spell.Spellbook)) {
                PFLog.Default.Error(context.AbilityBlueprint, string.Format("Spell in not in valid spellbook: {0}", context.AbilityBlueprint), Array.Empty<object>());
                return;
            }
            if (!AnySpellLevel && context.Ability.ParamSpellSlot.SpellLevel > SpellLevel) {
                PFLog.Default.Error(context.AbilityBlueprint, string.Format("Invalid target spell level {0}: {1}", context.Ability.ParamSpellSlot.SpellLevel, context.AbilityBlueprint), Array.Empty<object>());
                return;
            }
            var spellbook = context.Ability.ParamSpellSlot.Spell.Spellbook;
            spellbook.ForgetMemorized(context.Ability.ParamSpellSlot);
            spellbook.Memorize(context.Ability.m_ConvertedFrom);

            SpellSlot notAvailableSpellSlot = GetNotAvailableSpellSlot(context.Ability.m_ConvertedFrom);
            notAvailableSpellSlot.Available = true;

            using (context.GetDataScope(target)) {
                try {
                    ProvokeAoO.RunAction();
                } catch (Exception ex) {
                    ElementLogicException exception = (ex as ElementLogicException) ?? new ElementLogicException(ProvokeAoO, ex);
                    PFLog.Actions.Exception(exception, null, Array.Empty<object>());
                }
            }
        }

        public bool IsAbilityRestrictionPassed(AbilityData ability) {
            SpellSlot paramSpellSlot = ability.ParamSpellSlot;
            AbilityData abilityData = paramSpellSlot?.Spell;
            Spellbook spellbook = abilityData?.Spellbook;

            var unitBooks = ValidSpellbooks(ability.Caster);
            var validSpellbook = spellbook != null && unitBooks.Contains(spellbook);

            return abilityData != null && validSpellbook && (AnySpellLevel || paramSpellSlot?.SpellLevel <= SpellLevel) && (spellbook.Blueprint.IsArcanist || paramSpellSlot.Available);
        }

        public bool AddAsVarriant(SpellSlot slot, Spellbook book, UnitDescriptor unit) {
            var unitBooks = ValidSpellbooks(unit);
            var validSlot = book.Blueprint.IsArcanist || slot.Available;
            return unitBooks != null && unitBooks.Contains(book)
                && (AnySpellLevel || slot.SpellLevel <= SpellLevel)
                && validSlot;
        }
        public bool SpellQualifies(Spellbook book, int level, SpellSlot slot, AbilityData spell) {
            var OppositionDescriptors = book.OppositionDescriptors;
            var OppositionSchools = book.OppositionSchools;
            bool OpposedSlot = OppositionSchools.Contains(slot.Spell.Blueprint.School) || slot.Spell.Blueprint.SpellDescriptor.HasFlag(OppositionDescriptors);
            bool OpposedSpell = OppositionSchools.Contains(spell.Blueprint.School) || spell.Blueprint.SpellDescriptor.HasFlag(OppositionDescriptors);
            return (!book.Blueprint.IsArcanist || !book.m_MemorizedSpells[level].Any(s => s.Spell == spell && s.Available == true)) && (!OpposedSpell || OpposedSlot == OpposedSpell);
        }

        public string GetAbilityRestrictionUIText() {
            return "";
        }

        public IEnumerable<Spellbook> ValidSpellbooks(UnitDescriptor unit) {
            var Result = new HashSet<BlueprintCharacterClass>();
            foreach (ClassData classData in unit.Progression.Classes) {
                if (CharacterClass.HasReference(classData.CharacterClass)) {
                    if (Archetypes.Length > 0) {
                        foreach (BlueprintArchetypeReference archetype in Archetypes) {
                            BlueprintArchetype current = archetype.Get();
                            if ((!classData.CharacterClass.Archetypes.HasReference(current))
                                || classData.Archetypes.Contains(current)) {
                            }
                            Result.Add(classData.CharacterClass);
                        }
                    } else {
                        Result.Add(classData.CharacterClass);
                    }
                }
            }
            return Result.Select(c => unit.DemandSpellbook(c));
        }

        [CanBeNull]
        private static SpellSlot GetNotAvailableSpellSlot(AbilityData ability) {
            if (ability.Spellbook == null) {
                return null;
            }
            foreach (SpellSlot spellSlot in ability.Spellbook.GetMemorizedSpellSlots(ability.SpellLevel)) {
                if (!spellSlot.Available && spellSlot.Spell == ability) {
                    return spellSlot;
                }
            }
            return null;
        }

        public bool AnySpellLevel;

        [HideIf("AnySpellLevel")]
        public int SpellLevel;
        public BlueprintCharacterClassReference[] CharacterClass;
        public BlueprintArchetypeReference[] Archetypes;
        private ContextActionProvokeAttackOfOpportunity ProvokeAoO = Helpers.Create<ContextActionProvokeAttackOfOpportunity>(a => {
            a.ApplyToCaster = true;
        });

        [HarmonyPatch(typeof(AbilityData), "GetConversions")]
        static class AbilityData_GetConversions_QuickStudy_Patch {
            static void Postfix(AbilityData __instance, ref IEnumerable<AbilityData> __result) {
                List<AbilityData> list = __result.ToList();
                if (__instance.SpellSlot != null && __instance.Spellbook != null) {
                    foreach (Ability ability in __instance.Caster.Abilities) {
                        var QuickStudy = ability.Blueprint.GetComponent<QuickStudyComponent>();
                        if (QuickStudy?.AddAsVarriant(__instance.SpellSlot, __instance.Spellbook, __instance.Caster) ?? false) {
                            var knownSpellList = __instance.Spellbook.GetKnownSpells(__instance.SpellSlot.SpellLevel);
                            var customSpellList = __instance.Spellbook.GetCustomSpells(__instance.SpellSlot.SpellLevel);

                            IEnumerable<AbilityData> spellList;
                            if (!__instance.Spellbook.Blueprint.IsArcanist) {
                                spellList = knownSpellList.Concat(customSpellList).Where(s => !s.Equals(__instance.SpellSlot.Spell));
                            } else {
                                spellList = knownSpellList.Concat(customSpellList);
                            }
                            foreach (var spell in spellList.Where(s => QuickStudy.SpellQualifies(__instance.Spellbook, __instance.SpellLevel, __instance.SpellSlot, s))) {
                                AbilityData.AddAbilityUnique(ref list, new AbilityData(ability) {
                                    m_ConvertedFrom = spell,
                                    SaveSpellbookSlot = true,
                                    ParamSpellSlot = __instance.SpellSlot
                                });
                            };
                        }
                    }
                }
                __result = list;
            }
        }
        [HarmonyPatch(typeof(AbilityData), "IsAvailableInSpellbook", MethodType.Getter)]
        static class AbilityData_IsAvailableInSpellbook_QuickStudy_Patch {
            static void Postfix(AbilityData __instance, ref bool __result) {
                if (__instance.Blueprint.GetComponent<QuickStudyComponent>()) {
                    __result = true;
                }
            }
        }

        public class TooltipTemplateQuickStudy : TooltipTemplateAbility {

            public TooltipTemplateQuickStudy(AbilityData abilityData) : base(abilityData) {
                QuickStudyData = m_AbilityData;
                var abilityDataField = AccessTools.Field(typeof(TooltipTemplateQuickStudy), "m_AbilityData");
                var blueprintAbilityField = AccessTools.Field(typeof(TooltipTemplateQuickStudy), "BlueprintAbility");
                abilityDataField.SetValue(this, m_AbilityData.ConvertedFrom);
                blueprintAbilityField.SetValue(this, m_AbilityData.Blueprint);
            }

            public override void Prepare(TooltipTemplateType type) {
                base.Prepare(type);
                QuickStudyShortDescription = QuickStudyData.ShortenedDescription;
                QuickStudyDescription = QuickStudyData.Description;
                QuickStudyIcon = QuickStudyData.Icon;
                QuickStudyName = QuickStudyData.Name;
                QuickStudyType = LocalizedTexts.Instance.AbilityTypes.GetText(QuickStudyData.Blueprint.Type);
                QuickStudyActionTime = UIUtilityTexts.GetAbilityActionText(QuickStudyData.Blueprint, null);
            }

            public override IEnumerable<ITooltipBrick> GetHeader(TooltipTemplateType type) {
                yield return AddQuickStudyAbilityHeader();
                yield break;
            }

            public override IEnumerable<ITooltipBrick> GetBody(TooltipTemplateType type) {
                List<ITooltipBrick> list = new List<ITooltipBrick>();
                AddQuickStudyCastingTime(list);
                AddQuickStudyDescription(list, type);

                AddAbilityHeader(list);
                list.AddRange(base.GetBody(type));
                return list;
            }
            private void AddQuickStudyCastingTime(List<ITooltipBrick> bricks) {
                if (string.IsNullOrEmpty(this.m_ActionTime)) {
                    return;
                }
                string glossaryEntryName = UIUtility.GetGlossaryEntryName(TooltipElement.CastingTime.ToString());
                bricks.Add(new TooltipBrickIconValueStat(glossaryEntryName, QuickStudyActionTime, UIRoot.Instance.UIIcons.TimeIcon, TooltipIconValueStatType.Inverted, null));
                bricks.Add(new TooltipBrickSeparator(TooltipBrickElementType.Medium));
            }
            private void AddQuickStudyDescription(List<ITooltipBrick> bricks, TooltipTemplateType type) {
                string text = string.Empty;
                if (type != TooltipTemplateType.Tooltip) {
                    if (type == TooltipTemplateType.Info) {
                        text = QuickStudyDescription;
                    }
                } else {
                    text = QuickStudyShortDescription;
                }
                if (string.IsNullOrEmpty(text)) {
                    return;
                }
                bricks.Add(new TooltipBrickText(text, TooltipTextType.Paragraph));
                bricks.Add(new TooltipBrickSeparator(TooltipBrickElementType.Medium));
            }
            private ITooltipBrick AddQuickStudyAbilityHeader() {
                return new TooltipBrickEntityHeader(QuickStudyName, QuickStudyIcon, QuickStudyType, "", "");
            }
            private void AddAbilityHeader(List<ITooltipBrick> bricks) {
                bricks.Add(base.AddAbilityHeader());
            }

            private AbilityData QuickStudyData;
            private string QuickStudyName;
            private Sprite QuickStudyIcon;
            private string QuickStudyType;
            private string QuickStudyActionTime;
            private string QuickStudyShortDescription;
            private string QuickStudyDescription;
        }
    }
}
