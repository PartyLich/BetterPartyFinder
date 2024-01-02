using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Addon = Lumina.Excel.GeneratedSheets.Addon;

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

        private bool _visible;

        public bool Visible {
            get => this._visible;
            set => this._visible = value;
        }

        private bool _settingsVisible;

        public bool SettingsVisible {
            get => this._settingsVisible;
            set => this._settingsVisible = value;
        }

        private string DutySearchQuery { get; set; } = string.Empty;

        private string PresetName { get; set; } = string.Empty;

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.Interface.UiBuilder.Draw += this.Draw;
            this.Plugin.Interface.UiBuilder.OpenConfigUi += this.OnOpenConfig;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.Draw -= this.Draw;
            this.Plugin.Interface.UiBuilder.OpenConfigUi -= this.OnOpenConfig;
        }

        private void OnOpenConfig() {
            this.Visible = !this.Visible;
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

        private IntPtr PartyFinderAddon() {
            return this.Plugin.GameGui.GetAddonByName("LookingForGroup", 1);
        }

        private void Draw() {
            this.DrawFiltersWindow();
            this.DrawSettingsWindow();
        }

        private void DrawSettingsWindow() {
            ImGui.SetNextWindowSize(new Vector2(-1f, -1f), ImGuiCond.FirstUseEver);

            if (!this.SettingsVisible || !ImGui.Begin($"{Plugin.Name} settings", ref this._settingsVisible)) {
                return;
            }

            var openWithPf = this.Plugin.Config.ShowWhenPfOpen;
            if (ImGui.Checkbox("Open with PF", ref openWithPf)) {
                this.Plugin.Config.ShowWhenPfOpen = openWithPf;
                this.Plugin.Config.Save();
            }

            var sideOptions = new[] {
                "Left",
                "Right",
            };
            var sideIdx = this.Plugin.Config.WindowSide == WindowSide.Left ? 0 : 1;

            ImGui.TextUnformatted("Side of PF window to dock to");
            if (ImGui.Combo("###window-side", ref sideIdx, sideOptions, sideOptions.Length)) {
                this.Plugin.Config.WindowSide = sideIdx switch {
                    0 => WindowSide.Left,
                    1 => WindowSide.Right,
                    _ => this.Plugin.Config.WindowSide,
                };

                this.Plugin.Config.Save();
            }

            ImGui.Separator();

            var showDesc = this.Plugin.Config.ShowDescriptionOnJoin;
            if (ImGui.Checkbox("Show PF description in chat after joining", ref showDesc)) {
                this.Plugin.Config.ShowDescriptionOnJoin = showDesc;
                this.Plugin.Config.Save();
            }

            ImGui.End();
        }

        private unsafe void DrawFiltersWindow() {
            ImGui.SetNextWindowSize(new Vector2(550f, 510f), ImGuiCond.FirstUseEver);

            AtkUnitBase* addon = null;
            var addonPtr = this.PartyFinderAddon();
            if (this.Plugin.Config.ShowWhenPfOpen && addonPtr != IntPtr.Zero) {
                addon = (AtkUnitBase*) addonPtr;
            }

            var showWindow = this.Visible || addon != null && addon->IsVisible;

            if (!showWindow) {
                return;
            }

            if (!ImGui.Begin(Plugin.Name, ref this._visible, ImGuiWindowFlags.NoDocking)) {
                if (ImGui.IsWindowCollapsed() && addon != null && addon->IsVisible) {
                    // wait until addon is initialised to show
                    var rootNode = addon->RootNode;
                    if (rootNode == null) {
                        return;
                    }

                    ImGui.SetWindowPos(ImGuiHelpers.MainViewport.Pos + new Vector2(addon->X, addon->Y - ImGui.GetFrameHeight()));
                }

                ImGui.End();
                return;
            }

            if (addon != null && this.Plugin.Config.WindowSide == WindowSide.Right) {
                var rootNode = addon->RootNode;
                if (rootNode != null) {
                    ImGui.SetWindowPos(ImGuiHelpers.MainViewport.Pos + new Vector2(addon->X + rootNode->Width, addon->Y));
                }
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

                    this.Plugin.Common.Functions.PartyFinder.RefreshListings();
                }

                foreach (var preset in this.Plugin.Config.Presets) {
                    if (!ImGui.Selectable(preset.Value.Name)) {
                        continue;
                    }

                    this.Plugin.Config.SelectedPreset = preset.Key;
                    this.Plugin.Config.Save();

                    this.Plugin.Common.Functions.PartyFinder.RefreshListings();
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

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.Copy, "copy") && selected != null) {
                if (this.Plugin.Config.Presets.TryGetValue(selected.Value, out var copyFilter)) {
                    var guid = Guid.NewGuid();

                    var copied = copyFilter.Clone();
                    copied.Name += " (copy)";
                    this.Plugin.Config.Presets.Add(guid, copied);
                    this.Plugin.Config.SelectedPreset = guid;
                    this.Plugin.Config.Save();
                }
            }

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.Cog, "settings")) {
                this.SettingsVisible = true;
            }

            ImGui.Separator();

            if (selected != null && this.Plugin.Config.Presets.TryGetValue(selected.Value, out var filter)) {
                this.DrawPresetConfiguration(filter);
            }

            if (addon != null && this.Plugin.Config.WindowSide == WindowSide.Left) {
                var rootNode = addon->RootNode;
                if (rootNode != null) {
                    var currentWidth = ImGui.GetWindowWidth();
                    ImGui.SetWindowPos(ImGuiHelpers.MainViewport.Pos + new Vector2(addon->X - currentWidth, addon->Y));
                }
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

            this.DrawPlayersTab(filter);

            ImGui.EndTabBar();
        }

        private void DrawCategoriesTab(ConfigurationFilter filter) {
            if (!ImGui.BeginTabItem("Categories")) {
                return;
            }

            foreach (var category in (UiCategory[]) Enum.GetValues(typeof(UiCategory))) {
                var selected = filter.Categories.Contains(category);
                if (!ImGui.Selectable(category.Name(this.Plugin.DataManager), ref selected)) {
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
                var duties = this.Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()!
                    .Where(cf => cf.Unknown29)
                    .Where(cf => AllowedContentTypes.Contains(cf.ContentType.Row));

                var searchQuery = this.DutySearchQuery.Trim();
                if (searchQuery.Trim() != "") {
                    duties = duties.Where(duty => {
                        var sestring = (SeString) duty.Name;
                        return sestring.TextValue.ContainsIgnoreCase(searchQuery);
                    });
                }

                foreach (var cf in duties) {
                    var sestring = (SeString) cf.Name;
                    var selected = filter.Duties.Contains(cf.RowId);
                    var name = sestring.TextValue;
                    name = char.ToUpperInvariant(name[0]) + name[1..];
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

            if (ImGui.Button("Add slot")) {
                filter.Jobs.Add(0);
                this.Plugin.Config.Save();
            }

            var toRemove = new HashSet<int>();

            for (var i = 0; i < filter.Jobs.Count; i++) {
                var slot = filter.Jobs[i];

                if (!ImGui.CollapsingHeader($"Slot {i + 1}")) {
                    continue;
                }

                if (ImGui.Button("Select all")) {
                    filter.Jobs[i] = Enum.GetValues(typeof(JobFlags))
                        .Cast<JobFlags>()
                        .Aggregate(slot, (current, job) => current | job);
                    this.Plugin.Config.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button("Clear")) {
                    filter.Jobs[i] = 0;
                    this.Plugin.Config.Save();
                }

                ImGui.SameLine();

                if (ImGui.Button("Delete")) {
                    toRemove.Add(i);
                }

                foreach (var job in (JobFlags[]) Enum.GetValues(typeof(JobFlags))) {
                    var selected = (slot & job) > 0;
                    if (!ImGui.Selectable(job.ClassJob(this.Plugin.DataManager)?.Name ?? "???", ref selected)) {
                        continue;
                    }

                    if (selected) {
                        slot |= job;
                    } else {
                        slot &= ~job;
                    }

                    filter.Jobs[i] = slot;

                    this.Plugin.Config.Save();
                }
            }

            foreach (var idx in toRemove) {
                filter.Jobs.RemoveAt(idx);
            }

            if (toRemove.Count > 0) {
                this.Plugin.Config.Save();
            }

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

        private int _selectedWorld;
        private string _playerName = string.Empty;

        private void DrawPlayersTab(ConfigurationFilter filter) {
            var player = this.Plugin.ClientState.LocalPlayer;

            if (player == null || !ImGui.BeginTabItem("Players")) {
                return;
            }

            ImGui.PushItemWidth(ImGui.GetWindowWidth() / 3f);

            ImGui.InputText("###player-name", ref this._playerName, 64);

            ImGui.SameLine();

            var worlds = Util.WorldsOnDataCentre(this.Plugin.DataManager, player)
                .OrderBy(world => world.Name.RawString)
                .ToList();

            var worldNames = worlds
                .Select(world => world.Name.ToString())
                .ToArray();

            if (ImGui.Combo("###player-world", ref this._selectedWorld, worldNames, worldNames.Length)) {
            }

            ImGui.PopItemWidth();

            ImGui.SameLine();

            if (IconButton(FontAwesomeIcon.Plus, "add-player")) {
                var name = this._playerName.Trim();
                if (name.Length != 0) {
                    var world = worlds[this._selectedWorld];
                    filter.Players.Add(new PlayerInfo(name, world.RowId));
                    this.Plugin.Config.Save();
                }
            }

            PlayerInfo? deleting = null;

            foreach (var info in filter.Players) {
                var world = this.Plugin.DataManager.GetExcelSheet<World>()!.GetRow(info.World);
                ImGui.TextUnformatted($"{info.Name}@{world?.Name}");
                ImGui.SameLine();
                if (IconButton(FontAwesomeIcon.Trash, $"delete-player-{info.GetHashCode()}")) {
                    deleting = info;
                }
            }

            if (deleting != null) {
                filter.Players.Remove(deleting);
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
        internal static string? Name(this UiCategory category, IDataManager data) {
            var ct = data.GetExcelSheet<ContentType>()!;
            var addon = data.GetExcelSheet<Addon>()!;

            return category switch {
                UiCategory.None => addon.GetRow(1_562)?.Text.ToString(), // best guess
                UiCategory.DutyRoulette => ct.GetRow((uint) ContentType2.DutyRoulette)?.Name.ToString(),
                UiCategory.Dungeons => ct.GetRow((uint) ContentType2.Dungeons)?.Name.ToString(),
                UiCategory.Guildhests => ct.GetRow((uint) ContentType2.Guildhests)?.Name.ToString(),
                UiCategory.Trials => ct.GetRow((uint) ContentType2.Trials)?.Name.ToString(),
                UiCategory.Raids => ct.GetRow((uint) ContentType2.Raids)?.Name.ToString(),
                UiCategory.HighEndDuty => addon.GetRow(10_822)?.Text.ToString(), // best guess
                UiCategory.Pvp => ct.GetRow((uint) ContentType2.Pvp)?.Name.ToString(),
                UiCategory.QuestBattles => ct.GetRow((uint) ContentType2.QuestBattles)?.Name.ToString(),
                UiCategory.Fates => ct.GetRow((uint) ContentType2.Fates)?.Name.ToString(),
                UiCategory.TreasureHunt => ct.GetRow((uint) ContentType2.TreasureHunt)?.Name.ToString(),
                UiCategory.TheHunt => addon.GetRow(8_613)?.Text.ToString(),
                UiCategory.GatheringForays => addon.GetRow(2_306)?.Text.ToString(),
                UiCategory.DeepDungeons => ct.GetRow((uint) ContentType2.DeepDungeons)?.Name.ToString(),
                UiCategory.AdventuringForays => addon.GetRow(2_307)?.Text.ToString(),
                _ => null,
            };
        }

        internal static bool ListingMatches(this UiCategory category, IDataManager data, PartyFinderListing listing) {
            var cr = data.GetExcelSheet<ContentRoulette>()!;

            var isDuty = listing.Category == DutyCategory.Duty;
            var isNormal = listing.DutyType == DutyType.Normal;
            var isOther = listing.DutyType == DutyType.Other;
            var isNormalDuty = isNormal && isDuty;

            return category switch {
                UiCategory.None => isOther && isDuty && listing.RawDuty == 0,
                UiCategory.DutyRoulette => listing.DutyType == DutyType.Roulette && isDuty && (!cr.GetRow(listing.RawDuty)?.IsPvP ?? false),
                UiCategory.Dungeons => isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Dungeons,
                UiCategory.Guildhests => isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Guildhests,
                UiCategory.Trials => isNormalDuty && !listing.Duty.Value.HighEndDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Trials,
                UiCategory.Raids => isNormalDuty && !listing.Duty.Value.HighEndDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Raids,
                UiCategory.HighEndDuty => isNormalDuty && listing.Duty.Value.HighEndDuty,
                UiCategory.Pvp => listing.DutyType == DutyType.Roulette && isDuty && (cr.GetRow(listing.RawDuty)?.IsPvP ?? false)
                                  || isNormalDuty && listing.Duty.Value.ContentType.Row == (uint) ContentType2.Pvp,
                UiCategory.QuestBattles => isOther && listing.Category == DutyCategory.QuestBattles,
                UiCategory.Fates => isOther && listing.Category == DutyCategory.Fates,
                UiCategory.TreasureHunt => isOther && listing.Category == DutyCategory.TreasureHunt,
                UiCategory.TheHunt => isOther && listing.Category == DutyCategory.TheHunt,
                UiCategory.GatheringForays => isNormal && listing.Category == DutyCategory.GatheringForays,
                UiCategory.DeepDungeons => isOther && listing.Category == DutyCategory.DeepDungeons,
                UiCategory.AdventuringForays => isNormal && listing.Category == DutyCategory.AdventuringForays,
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
