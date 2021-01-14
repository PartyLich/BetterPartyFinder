﻿using System;
using System.Linq;
using System.Numerics;
using Dalamud.Data;
using Dalamud.Interface;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace BetterPartyFinder {
    public class PluginUi : IDisposable {
        private static readonly uint[] AllowedContentTypes = {
            2,
            3,
            4,
            5,
            6,
            16,
            21,
            26,
            28,
        };

        private Plugin Plugin { get; }

        private string DutySearchQuery { get; set; } = string.Empty;

        private string PresetName { get; set; } = string.Empty;

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.Interface.UiBuilder.OnBuildUi += this.Draw;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.OnBuildUi -= this.Draw;
        }

        private static bool IconButton(FontAwesomeIcon icon, string? id = null) {
            ImGui.PushFont(UiBuilder.IconFont);

            var text = icon.ToIconString();
            if (id != null) {
                text += $"##{id}";
            }

            var result = ImGui.Button(text);

            ImGui.PopFont();

            return result;
        }

        private void Draw() {
            if (!ImGui.Begin($"{this.Plugin.Name} settings")) {
                return;
            }

            var selected = this.Plugin.Config.SelectedPreset;

            string selectedName;
            if (selected == null) {
                selectedName = "<none>";
            } else {
                if (this.Plugin.Config.Presets.TryGetValue(selected.Value, out var preset)) {
                    selectedName = preset.Name;
                } else {
                    this.Plugin.Config.SelectedPreset = null;
                    selectedName = "<invalid preset>";
                }
            }

            ImGui.TextUnformatted("Preset");
            if (ImGui.BeginCombo("###preset", selectedName)) {
                if (ImGui.Selectable("<none>")) {
                    this.Plugin.Config.SelectedPreset = null;
                    this.Plugin.Config.Save();
                }

                foreach (var preset in this.Plugin.Config.Presets) {
                    if (!ImGui.Selectable(preset.Value.Name)) {
                        continue;
                    }

                    this.Plugin.Config.SelectedPreset = preset.Key;
                    this.Plugin.Config.Save();
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.Plus, "add-preset")) {
                var id = Guid.NewGuid();

                this.Plugin.Config.Presets.Add(id, ConfigurationFilter.Create());
                this.Plugin.Config.SelectedPreset = id;
                this.Plugin.Config.Save();
            }

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.Trash, "delete-preset") && selected != null) {
                this.Plugin.Config.Presets.Remove(selected.Value);
                this.Plugin.Config.Save();
            }

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.PencilAlt, "edit-preset") && selected != null) {
                if (this.Plugin.Config.Presets.TryGetValue(selected.Value, out var editPreset)) {
                    this.PresetName = editPreset.Name;

                    ImGui.OpenPopup("###rename-preset");
                }
            }

            if (ImGui.BeginPopupModal("Rename preset###rename-preset")) {
                if (selected != null && this.Plugin.Config.Presets.TryGetValue(selected.Value, out var editPreset)) {
                    ImGui.TextUnformatted("Preset name");
                    ImGui.PushItemWidth(-1f);
                    var name = this.PresetName;
                    if (ImGui.InputText("###preset-name", ref name, 1_000)) {
                        this.PresetName = name;
                    }

                    ImGui.PopItemWidth();

                    if (ImGui.Button("Save") && this.PresetName.Trim().Length > 0) {
                        editPreset.Name = this.PresetName;
                        this.Plugin.Config.Save();
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();

            if (selected != null && this.Plugin.Config.Presets.TryGetValue(selected.Value, out var filter)) {
                this.DrawPresetConfiguration(filter);
            }

            ImGui.End();
        }

        private void DrawPresetConfiguration(ConfigurationFilter filter) {
            if (!ImGui.BeginTabBar("bpf-tabs")) {
                return;
            }

            this.DrawCategoriesTab(filter);

            this.DrawDutiesTab(filter);

            this.DrawItemLevelTab(filter);

            this.DrawJobsTab(filter);

            this.DrawRestrictionsTab(filter);

            ImGui.EndTabBar();
        }

        private void DrawCategoriesTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Categories")) {
                return;
            }

            foreach (var category in (UiCategory[]) Enum.GetValues(typeof(UiCategory))) {
                var selected = filter.Categories.Contains(category);
                if (!ImGui.Selectable(category.Name(this.Plugin.Interface.Data), ref selected)) {
                    continue;
                }

                if (selected) {
                    filter.Categories.Add(category);
                } else {
                    filter.Categories.Remove(category);
                }

                this.Plugin.Config.Save();
            }


            ImGui.EndTabItem();
        }

        private void DrawDutiesTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Duties")) {
                return;
            }

            var listModeStrings = new[] {
                "Show ONLY these duties",
                "Do NOT show these duties",
            };
            var listModeIdx = filter.DutiesMode == ListMode.Blacklist ? 1 : 0;
            ImGui.TextUnformatted("List mode");
            ImGui.PushItemWidth(-1);
            if (ImGui.Combo("###list-mode", ref listModeIdx, listModeStrings, listModeStrings.Length)) {
                filter.DutiesMode = listModeIdx == 0 ? ListMode.Whitelist : ListMode.Blacklist;
                this.Plugin.Config.Save();
            }

            ImGui.PopItemWidth();

            var query = this.DutySearchQuery;
            ImGui.TextUnformatted("Search");
            if (ImGui.InputText("###search", ref query, 1_000)) {
                this.DutySearchQuery = query;
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear list")) {
                filter.Duties.Clear();
                this.Plugin.Config.Save();
            }

            if (ImGui.BeginChild("duty-selection", new Vector2(-1f, -1f))) {
                var duties = this.Plugin.Interface.Data.GetExcelSheet<ContentFinderCondition>()
                    .Where(cf => cf.Unknown29)
                    .Where(cf => AllowedContentTypes.Contains(cf.ContentType.Row));

                var searchQuery = this.DutySearchQuery.Trim();
                if (searchQuery.Trim() != "") {
                    duties = duties.Where(duty => {
                        var sestring = this.Plugin.Interface.SeStringManager.Parse(duty.Name.RawData.ToArray());
                        return sestring.TextValue.ContainsIgnoreCase(searchQuery);
                    });
                }

                foreach (var cf in duties) {
                    var sestring = this.Plugin.Interface.SeStringManager.Parse(cf.Name.RawData.ToArray());
                    var selected = filter.Duties.Contains(cf.RowId);
                    var name = sestring.TextValue;
                    name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                    if (!ImGui.Selectable(name, ref selected)) {
                        continue;
                    }

                    if (selected) {
                        filter.Duties.Add(cf.RowId);
                    } else {
                        filter.Duties.Remove(cf.RowId);
                    }

                    this.Plugin.Config.Save();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }

        private void DrawItemLevelTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Item level")) {
                return;
            }

            var hugePfs = filter.AllowHugeItemLevel;
            if (ImGui.Checkbox("Show PFs above maximum item level", ref hugePfs)) {
                filter.AllowHugeItemLevel = hugePfs;
                this.Plugin.Config.Save();
            }

            var minLevel = (int?) filter.MinItemLevel ?? 0;
            ImGui.TextUnformatted("Minimum item level (0 to disable)");
            ImGui.PushItemWidth(-1);
            if (ImGui.InputInt("###min-ilvl", ref minLevel)) {
                filter.MinItemLevel = minLevel == 0 ? null : (uint) minLevel;
                this.Plugin.Config.Save();
            }

            ImGui.PopItemWidth();

            var maxLevel = (int?) filter.MaxItemLevel ?? 0;
            ImGui.TextUnformatted("Maximum item level (0 to disable)");
            ImGui.PushItemWidth(-1);
            if (ImGui.InputInt("###max-ilvl", ref maxLevel)) {
                filter.MaxItemLevel = maxLevel == 0 ? null : (uint) maxLevel;
                this.Plugin.Config.Save();
            }

            ImGui.PopItemWidth();

            ImGui.EndTabItem();
        }

        private void DrawJobsTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Jobs")) {
                return;
            }

            ImGui.TextUnformatted("Nothing here yet");

            ImGui.EndTabItem();
        }

        private void DrawRestrictionsTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Restrictions")) {
                return;
            }

            var practice = filter[ObjectiveFlags.Practice];
            if (ImGui.Checkbox("Practice", ref practice)) {
                filter[ObjectiveFlags.Practice] = practice;
                this.Plugin.Config.Save();
            }

            var dutyCompletion = filter[ObjectiveFlags.DutyCompletion];
            if (ImGui.Checkbox("Duty completion", ref dutyCompletion)) {
                filter[ObjectiveFlags.DutyCompletion] = dutyCompletion;
                this.Plugin.Config.Save();
            }

            var loot = filter[ObjectiveFlags.Loot];
            if (ImGui.Checkbox("Loot", ref loot)) {
                filter[ObjectiveFlags.Loot] = loot;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var noCondition = filter[ConditionFlags.None];
            if (ImGui.Checkbox("No duty completion requirement", ref noCondition)) {
                filter[ConditionFlags.None] = noCondition;
                this.Plugin.Config.Save();
            }

            var dutyIncomplete = filter[ConditionFlags.DutyIncomplete];
            if (ImGui.Checkbox("Duty incomplete", ref dutyIncomplete)) {
                filter[ConditionFlags.DutyIncomplete] = dutyIncomplete;
                this.Plugin.Config.Save();
            }

            var dutyComplete = filter[ConditionFlags.DutyComplete];
            if (ImGui.Checkbox("Duty complete", ref dutyComplete)) {
                filter[ConditionFlags.DutyComplete] = dutyComplete;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var undersized = filter[DutyFinderSettingsFlags.UndersizedParty];
            if (ImGui.Checkbox("Undersized party", ref undersized)) {
                filter[DutyFinderSettingsFlags.UndersizedParty] = undersized;
                this.Plugin.Config.Save();
            }

            var minItemLevel = filter[DutyFinderSettingsFlags.MinimumItemLevel];
            if (ImGui.Checkbox("Minimum item level", ref minItemLevel)) {
                filter[DutyFinderSettingsFlags.MinimumItemLevel] = minItemLevel;
                this.Plugin.Config.Save();
            }

            var silenceEcho = filter[DutyFinderSettingsFlags.SilenceEcho];
            if (ImGui.Checkbox("Silence Echo", ref silenceEcho)) {
                filter[DutyFinderSettingsFlags.SilenceEcho] = silenceEcho;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var greedOnly = filter[LootRuleFlags.GreedOnly];
            if (ImGui.Checkbox("Greed only", ref greedOnly)) {
                filter[LootRuleFlags.GreedOnly] = greedOnly;
                this.Plugin.Config.Save();
            }

            var lootmaster = filter[LootRuleFlags.Lootmaster];
            if (ImGui.Checkbox("Lootmaster", ref lootmaster)) {
                filter[LootRuleFlags.Lootmaster] = lootmaster;
                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var dataCentre = filter[SearchAreaFlags.DataCentre];
            if (ImGui.Checkbox("Data centre parties", ref dataCentre)) {
                filter[SearchAreaFlags.DataCentre] = dataCentre;
                this.Plugin.Config.Save();
            }

            var world = filter[SearchAreaFlags.World];
            if (ImGui.Checkbox("World-local parties", ref world)) {
                filter[SearchAreaFlags.World] = world;
                this.Plugin.Config.Save();
            }

            var onePlayerPer = filter[SearchAreaFlags.OnePlayerPerJob];
            if (ImGui.Checkbox("One player per job", ref onePlayerPer)) {
                filter[SearchAreaFlags.OnePlayerPerJob] = onePlayerPer;
                this.Plugin.Config.Save();
            }

            ImGui.EndTabItem();
        }
    }

    public enum UiCategory {
        None,
        DutyRoulette,
        Dungeons,
        Guildhests,
        Trials,
        Raids,
        HighEndDuty,
        Pvp,
        QuestBattles,
        Fates,
        TreasureHunt,
        TheHunt,
        GatheringForays,
        DeepDungeons,
        AdventuringForays,
    }

    internal static class UiCategoryExt {
        internal static string? Name(this UiCategory category, DataManager data) {
            var ct = data.GetExcelSheet<ContentType>();
            var addon = data.GetExcelSheet<Addon>();

            return category switch {
                UiCategory.None => addon.GetRow(1_562).Text.ToString(), // best guess
                UiCategory.DutyRoulette => ct.GetRow((uint) ContentType2.DutyRoulette).Name.ToString(),
                UiCategory.Dungeons => ct.GetRow((uint) ContentType2.Dungeons).Name.ToString(),
                UiCategory.Guildhests => ct.GetRow((uint) ContentType2.Guildhests).Name.ToString(),
                UiCategory.Trials => ct.GetRow((uint) ContentType2.Trials).Name.ToString(),
                UiCategory.Raids => ct.GetRow((uint) ContentType2.Raids).Name.ToString(),
                UiCategory.HighEndDuty => addon.GetRow(10_822).Text.ToString(), // best guess
                UiCategory.Pvp => ct.GetRow((uint) ContentType2.Pvp).Name.ToString(),
                UiCategory.QuestBattles => ct.GetRow((uint) ContentType2.QuestBattles).Name.ToString(),
                UiCategory.Fates => ct.GetRow((uint) ContentType2.Fates).Name.ToString(),
                UiCategory.TreasureHunt => ct.GetRow((uint) ContentType2.TreasureHunt).Name.ToString(),
                UiCategory.TheHunt => addon.GetRow(8_613).Text.ToString(),
                UiCategory.GatheringForays => addon.GetRow(2_306).Text.ToString(),
                UiCategory.DeepDungeons => ct.GetRow((uint) ContentType2.DeepDungeons).Name.ToString(),
                UiCategory.AdventuringForays => addon.GetRow(2_307).Text.ToString(),
                _ => null,
            };
        }

        internal static bool ListingMatches(this UiCategory category, DataManager data, PartyFinderListing listing) {
            var cr = data.GetExcelSheet<ContentRoulette>();

            var isDuty = listing.Category == Category.Duty;
            var isNormal = listing.DutyType == DutyType.Normal;
            var isOther = listing.DutyType == DutyType.Other;
            var isNormalDuty = isNormal && isDuty;

            return category switch {
                UiCategory.None => isOther && isDuty && listing.RawDuty == 0,
                UiCategory.DutyRoulette => listing.DutyType == DutyType.Roulette && isDuty && !cr.GetRow(listing.RawDuty).Unknown10,
                UiCategory.Dungeons => isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Dungeons,
                UiCategory.Guildhests => isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Guildhests,
                UiCategory.Trials => isNormalDuty && !listing.Duty.Value.HighEndDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Trials,
                UiCategory.Raids => isNormalDuty && !listing.Duty.Value.HighEndDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Raids,
                UiCategory.HighEndDuty => isNormalDuty && listing.Duty.Value.HighEndDuty,
                UiCategory.Pvp => listing.DutyType == DutyType.Roulette && isDuty && cr.GetRow(listing.RawDuty).Unknown10
                                  || isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Pvp,
                UiCategory.QuestBattles => isOther && listing.Category == Category.QuestBattles,
                UiCategory.Fates => isOther && listing.Category == Category.Fates,
                UiCategory.TreasureHunt => isOther && listing.Category == Category.TreasureHunt,
                UiCategory.TheHunt => isOther && listing.Category == Category.TheHunt,
                UiCategory.GatheringForays => isNormal && listing.Category == Category.GatheringForays,
                UiCategory.DeepDungeons => isOther && listing.Category == Category.DeepDungeons,
                UiCategory.AdventuringForays => isNormal && listing.Category == Category.AdventuringForays,
                _ => false,
            };
        }

        private enum ContentType2 {
            DutyRoulette = 1,
            Dungeons = 2,
            Guildhests = 3,
            Trials = 4,
            Raids = 5,
            Pvp = 6,
            QuestBattles = 7,
            Fates = 8,
            TreasureHunt = 9,
            Levequests = 10,
            GrandCompany = 11,
            Companions = 12,
            BeastTribeQuests = 13,
            OverallCompletion = 14,
            PlayerCommendation = 15,
            DisciplesOfTheLand = 16,
            DisciplesOfTheHand = 17,
            RetainerVentures = 18,
            GoldSaucer = 19,
            DeepDungeons = 21,
            WondrousTails = 24,
            CustomDeliveries = 25,
            Eureka = 26,
            UltimateRaids = 28,
        }
    }
}