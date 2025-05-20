using BepInEx.Logging;
using Menu.Remix;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;
using MoreSlugcats;
using System.Collections.Generic;
using UnityEngine;
using Watcher;

namespace ScaredWatcher;

public class ConfigOptions : OptionInterface
{
    public ConfigOptions()
    {
        GhostFright = this.config.Bind<float>("GhostFright", 4f, new ConfigAcceptableRange<float>(0f, 10f));
        RotFright = this.config.Bind<float>("RotFright", 1f, new ConfigAcceptableRange<float>(0f, 10f));
        WeirdnessFright = this.config.Bind<float>("WeirdnessFright", 0.7f, new ConfigAcceptableRange<float>(0f, 10f));
        CreatureFright = this.config.Bind<float>("CreatureFright", 0.3f, new ConfigAcceptableRange<float>(0f, 10f));
        SoundFright = this.config.Bind<float>("SoundFright", 0.7f, new ConfigAcceptableRange<float>(0f, 10f));
        MaxIntensity = this.config.Bind<float>("MaxIntensity", 1.5f, new ConfigAcceptableRange<float>(0f, 10f));
        RecoverySpeed = this.config.Bind<float>("RecoverySpeed", 1f, new ConfigAcceptableRange<float>(0f, 10f));
        OnlyWhileMoving = this.config.Bind<bool>("OnlyWhileMoving", false);
        NoReversing = this.config.Bind<bool>("NoReversing", false);

        AllSlugcats = this.config.Bind<bool>("AllSlugcats", false);
    }

    //General
    public readonly Configurable<float> GhostFright;
    public readonly Configurable<float> RotFright;
    public readonly Configurable<float> WeirdnessFright;
    public readonly Configurable<float> CreatureFright;
    public readonly Configurable<float> SoundFright;
    public readonly Configurable<float> MaxIntensity;
    public readonly Configurable<float> RecoverySpeed;
    public readonly Configurable<bool> OnlyWhileMoving;
    public readonly Configurable<bool> NoReversing;

    //Slugcats
    public readonly Configurable<bool> AllSlugcats;
    public readonly Dictionary<string, Configurable<bool>> SlugcatsEnabled = new();

    //private OpCheckBox OnlyCancelCheckbox;
    //private OpCheckBox NoReversingCheckbox;

    public override void Initialize()
    {
        var intensitiesTab = new OpTab(this, "Intensities");
        var slugcatsTab = new OpTab(this, "Slugcats");
        this.Tabs = new[]
        {
            intensitiesTab,
            slugcatsTab
        };

        float t = 150f, y = 500f, h = -50f, x = 50f, w = 80f;

        //OnlyCancelCheckbox = new OpCheckBox(OnlyCancelInputs, x, y) { description = "ONLY allows random inputs while you are moving. This allows Watcher to stay still easily; only panicking while moving." };
        //NoReversingCheckbox = new OpCheckBox(NoReversing, x, y) { description = "Prevents random inputs from reversing your direction while you're already moving. Instead of reversing direction, they will force a lack of movement." };

        //General Options
        intensitiesTab.AddItems(
            new OpLabel(t, y, "Echo Fright"),
            new OpUpdown(GhostFright, new(x, y), w, 2),
            new OpLabel(t, y+=h, "Rot Fright"),
            new OpUpdown(RotFright, new(x, y), w, 2),
            new OpLabel(t, y+=h, "Strange Effects Fright"),
            new OpUpdown(WeirdnessFright, new(x, y), w, 2),
            new OpLabel(t, y+=h, "General Creature Fright"),
            new OpUpdown(CreatureFright, new(x, y), w, 2),
            new OpLabel(t, y+=h, "Sudden Sound Fright"),
            new OpUpdown(SoundFright, new(x, y), w, 2),
            new OpLabel(t, y+=h, "MAXIMUM Effect Intensity"),
            new OpUpdown(MaxIntensity, new(x, y), w, 2) { description = "Set to 0.9 or below to entirely prevent it from giving random inputs.\nBasic guide: 1.5 = up to 1/3 inputs random (at max intensity), 2.0 = up to 1/2, 3.0 = up to 2/3, 4.0 = up to 3/4, etc." },
            new OpLabel(t, y += h, "Recovery Speed"),
            new OpUpdown(RecoverySpeed, new(x, y), w, 2) { description = "Scales how quickly the Watcher becomes accustomed to and recovers from frights." },

            new OpLabel(t, y += h, "Only While Moving"),
            new OpCheckBox(OnlyWhileMoving, x, y) { description = "ONLY allows random inputs while you are moving. This allows Watcher to stay still easily; only panicking while moving." },
            new OpLabel(t, y += h, "Don't Reverse Direction"),
            new OpCheckBox(NoReversing, x, y) { description = "Prevents random inputs from reversing your direction while you're already moving. Instead of reversing direction, they will force a lack of movement." }
            );

        SetupSlugcatConfigs();

        t = 100f; y = 550f; h = -30f;

        slugcatsTab.AddItems(
            new OpLabel(t, y, "Enable for All Slugcats"),
            new OpCheckBox(AllSlugcats, x, y)
            );

        y = 530f;
        foreach (var kvp in SlugcatsEnabled)
        {
            var name = SlugcatStats.getSlugcatName(new SlugcatStats.Name(kvp.Key));
            slugcatsTab.AddItems(
                new OpLabel(t, y += h, name),
                new OpCheckBox(kvp.Value, x, y)
                );
        }
    }

    /*public override void Update()
    {
        if (OnlyCancelCheckbox != null && OnlyCancelCheckbox.GetValueBool())
        {
            NoReversingCheckbox.greyedOut = true;
            NoReversingCheckbox.SetValueBool(true);
        }
    }*/

    public void SetupSlugcatConfigs()
    {
        foreach (var scug in SlugcatStats.Name.values.entries)
        {
            if (!SlugcatsEnabled.ContainsKey(scug) && !scug.StartsWith("JollyPlayer"))
            {
                var config = this.config.Bind<bool>(scug + "_enabled", ScugDefaultEnabled(scug));
                SlugcatsEnabled.Add(scug, config);
            }
        }
    }

    private bool ScugDefaultEnabled(string scug)
    {
        return ModManager.Watcher && scug == WatcherEnums.SlugcatStatsName.Watcher.value
            || ModManager.MSC && scug == MoreSlugcatsEnums.SlugcatStatsName.Slugpup.value;
    }
}