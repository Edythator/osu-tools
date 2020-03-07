// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Alba.CsConsoleFormat;
using JetBrains.Annotations;
using McMaster.Extensions.CommandLineUtils;
using MySql.Data.MySqlClient;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace PerformanceCalculator.Profile
{
    [Command(Name = "profile", Description = "Computes the total performance (pp) of a profile.")]
    public class ProfileCommand : ProcessorCommand
    {
        [UsedImplicitly]
        [Required]
        [Argument(0, Name = "user", Description = "User ID is preferred, but username should also work.")]
        public string ProfileName { get; }

        [UsedImplicitly]
        [Required]
        [Argument(1, Name = "api key", Description = "API Key, which you can get from here: https://osu.ppy.sh/p/api")]
        public string Key { get; }

        [UsedImplicitly]
        [Option(Template = "-r|--ruleset:<ruleset-id>", Description = "The ruleset to compute the profile for. 0 - osu!, 1 - osu!taiko, 2 - osu!catch, 3 - osu!mania. Defaults to osu!.")]
        [AllowedValues("0", "1", "2", "3")]
        public int? Ruleset { get; }

        [UsedImplicitly]
        [Option(Template = "-db|--database", Description = "sets whether to use a database or not")]
        [AllowedValues("false", "true")]
        public string Database { get; }

        private const string base_url = "https://osu.ppy.sh";

        public override void Execute()
        {
            if (Database == "false")
            {
                var displayPlays = new List<UserPlayInfo>();

                var ruleset = LegacyHelper.GetRulesetFromLegacyID(Ruleset ?? 0);

                Console.WriteLine("Getting user data...");
                dynamic userData = getJsonFromApi($"get_user?k={Key}&u={ProfileName}&m={Ruleset}")[0];

                Console.WriteLine("Getting user top scores...");

                foreach (var play in getJsonFromApi($"get_user_best?k={Key}&u={ProfileName}&m={Ruleset}&limit=100"))
                {
                    string beatmapID = play.beatmap_id;

                    string cachePath = Path.Combine("cache", $"{beatmapID}.osu");

                    if (!File.Exists(cachePath))
                    {
                        Console.WriteLine($"Downloading {beatmapID}.osu...");
                        new FileWebRequest(cachePath, $"{base_url}/osu/{beatmapID}").Perform();
                    }

                    Mod[] mods = ruleset.ConvertLegacyMods((LegacyMods)play.enabled_mods).ToArray();

                    var working = new ProcessorWorkingBeatmap(cachePath, (int)play.beatmap_id);

                    var score = new ProcessorScoreParser(working).Parse(new ScoreInfo
                    {
                        Ruleset = ruleset.RulesetInfo,
                        MaxCombo = play.maxcombo,
                        Mods = mods,
                        Statistics = new Dictionary<HitResult, int>
                    {
                        { HitResult.Perfect, (int)play.countgeki },
                        { HitResult.Great, (int)play.count300 },
                        { HitResult.Good, (int)play.count100 },
                        { HitResult.Ok, (int)play.countkatu },
                        { HitResult.Meh, (int)play.count50 },
                        { HitResult.Miss, (int)play.countmiss }
                    }
                    });

                    var thisPlay = new UserPlayInfo
                    {
                        Beatmap = working.BeatmapInfo,
                        LocalPP = ruleset.CreatePerformanceCalculator(working, score.ScoreInfo).Calculate(),
                        LivePP = play.pp,
                        Mods = mods.Length > 0 ? mods.Select(m => m.Acronym).Aggregate((c, n) => $"{c}, {n}") : "None"
                    };

                    displayPlays.Add(thisPlay);
                }

                var localOrdered = displayPlays.OrderByDescending(p => p.LocalPP).ToList();
                var liveOrdered = displayPlays.OrderByDescending(p => p.LivePP).ToList();

                int index = 0;
                double totalLocalPP = localOrdered.Sum(play => Math.Pow(0.95, index++) * play.LocalPP);
                double totalLivePP = userData.pp_raw;

                index = 0;
                double nonBonusLivePP = liveOrdered.Sum(play => Math.Pow(0.95, index++) * play.LivePP);

                //todo: implement properly. this is pretty damn wrong.
                var playcountBonusPP = (totalLivePP - nonBonusLivePP);
                totalLocalPP += playcountBonusPP;
                double totalDiffPP = totalLocalPP - totalLivePP;

                OutputDocument(new Document(
                    new Span($"User:     {userData.username}"), "\n",
                    new Span($"Live PP:  {totalLivePP:F1} (including {playcountBonusPP:F1}pp from playcount)"), "\n",
                    new Span($"Local PP: {totalLocalPP:F1} ({totalDiffPP:+0.0;-0.0;-})"), "\n",
                    new Grid
                    {
                        Columns = { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto },
                        Children =
                        {
                        new Cell("beatmap"),
                        new Cell("live pp"),
                        new Cell("local pp"),
                        new Cell("pp change"),
                        new Cell("position change"),
                        localOrdered.Select(item => new[]
                        {
                            new Cell($"{item.Beatmap.OnlineBeatmapID} - {item.Beatmap}"),
                            new Cell($"{item.LivePP:F1}") { Align = Align.Right },
                            new Cell($"{item.LocalPP:F1}") { Align = Align.Right },
                            new Cell($"{item.LocalPP - item.LivePP:F1}") { Align = Align.Right },
                            new Cell($"{liveOrdered.IndexOf(item) - localOrdered.IndexOf(item):+0;-0;-}") { Align = Align.Center },
                        })
                        }
                    }
                ));
            }
            else
            {
                var displayPlays = new List<UserPlayInfo>();

                var ruleset = LegacyHelper.GetRulesetFromLegacyID(Ruleset ?? 0);

                Console.WriteLine("Getting user data...");
                dynamic userData = getJsonFromApi($"get_user?k={Key}&u={ProfileName}&m={Ruleset}")[0];

                string[] config = { };

                if (File.Exists("db.cfg"))
                    config = File.ReadAllLines("db.cfg");
                else
                {
                    File.AppendAllLines("db.cfg", new string[] { "server=", "database=", "mysqlUsername=", "mySqlPassword=" });
                    Console.WriteLine("config file generated, fill it in, and rerun this program");
                    Environment.Exit(-1);
                }

                MySqlConnection connection = new MySqlConnection($"server={config[0].Split("=")[1]};database={config[1].Split("=")[1]};uid={config[2].Split("=")[1]};password={config[3].Split("=")[1]}");
                MySqlCommand cmd = new MySqlCommand($"SELECT * FROM `osu_scores_high` WHERE `user_id` = {userData.user_id}", connection);
                connection.Open();
                MySqlDataReader rdr = cmd.ExecuteReader();

                while (rdr.Read())
                {
                    int pp = 0;
                    try
                    {
                        pp = rdr.GetInt32("pp");
                        string beatmap_id = rdr.GetString("beatmap_id");

                        string[] blacklist = { "1257904" };

                        if (beatmap_id != blacklist[0])
                        File.AppendAllText("plays.txt", $"{beatmap_id}|{rdr.GetString("enabled_mods")}|{rdr.GetInt32("maxcombo")}|{rdr.GetInt32("countgeki")}|{rdr.GetInt32("count300")}|{rdr.GetInt32("count100")}|{rdr.GetInt32("countkatu")}|{rdr.GetInt32("count50")}|{rdr.GetInt32("countmiss")}|{pp}\n");
                    }
                    catch (Exception e)
                    {
                        if (e.Message.Contains("null"))
                            pp = 0;
                        continue;
                    }
                }

                string[] buffer = File.ReadAllLines("plays.txt");
                File.Delete("plays.txt");

                foreach (string s in buffer)
                {
                    string[] split = s.Split('|');
                    string beatmapID = split[0];
                    string cachePath = Path.Combine("cache", $"{beatmapID}.osu");

                    if (!File.Exists(cachePath))
                    {
                        Console.WriteLine($"Downloading {beatmapID}.osu...");
                        new FileWebRequest(cachePath, $"{base_url}/osu/{beatmapID}").Perform();
                    }

                    Mod[] mods = ruleset.ConvertLegacyMods((LegacyMods)int.Parse(split[1])).ToArray();

                    var working = new ProcessorWorkingBeatmap(cachePath, int.Parse(split[0]));

                    var score = new ProcessorScoreParser(working).Parse(new ScoreInfo
                    {
                        Ruleset = ruleset.RulesetInfo,
                        MaxCombo = int.Parse(split[2]),
                        Mods = mods,
                        Statistics = new Dictionary<HitResult, int>
                    {
                        { HitResult.Perfect, int.Parse(split[3]) },
                        { HitResult.Great, int.Parse(split[4]) },
                        { HitResult.Good, int.Parse(split[5]) },
                        { HitResult.Ok, int.Parse(split[6]) },
                        { HitResult.Meh, int.Parse(split[7])},
                        { HitResult.Miss, int.Parse(split[8]) }
                    }
                    });

                    var thisPlay = new UserPlayInfo
                    {
                        Beatmap = working.BeatmapInfo,
                        LocalPP = ruleset.CreatePerformanceCalculator(working, score.ScoreInfo).Calculate(),
                        LivePP = int.Parse(split[9]),
                        Mods = mods.Length > 0 ? mods.Select(m => m.Acronym).Aggregate((c, n) => $"{c}, {n}") : "None"
                    };
                    displayPlays.Add(thisPlay);
                }
                var localOrdered = displayPlays.OrderByDescending(p => p.LocalPP).ToList();
                var liveOrdered = displayPlays.OrderByDescending(p => p.LivePP).ToList();

                int index = 0;
                double totalLocalPP = localOrdered.Sum(play => Math.Pow(0.95, index++) * play.LocalPP);
                double totalLivePP = userData.pp_raw;

                index = 0;
                double nonBonusLivePP = liveOrdered.Sum(play => Math.Pow(0.95, index++) * play.LivePP);

                //todo: implement properly. this is pretty damn wrong.
                var playcountBonusPP = (totalLivePP - nonBonusLivePP);
                totalLocalPP += playcountBonusPP;
                double totalDiffPP = totalLocalPP - totalLivePP;

                OutputDocument(new Document(
                    new Span($"User:     {userData.username}"), "\n",
                    new Span($"Live PP:  {totalLivePP:F1} (including {playcountBonusPP:F1}pp from playcount)"), "\n",
                    new Span($"Local PP: {totalLocalPP:F1} ({totalDiffPP:+0.0;-0.0;-})"), "\n",
                    new Grid
                    {
                        Columns = { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto },
                        Children =
                        {
                        new Cell("beatmap"),
                        new Cell("live pp"),
                        new Cell("local pp"),
                        new Cell("pp change"),
                        new Cell("position change"),
                        localOrdered.Select(item => new[]
                        {
                            new Cell($"{item.Beatmap.OnlineBeatmapID} - {item.Beatmap}"),
                            new Cell($"{item.LivePP:F1}") { Align = Align.Right },
                            new Cell($"{item.LocalPP:F1}") { Align = Align.Right },
                            new Cell($"{item.LocalPP - item.LivePP:F1}") { Align = Align.Right },
                            new Cell($"{liveOrdered.IndexOf(item) - localOrdered.IndexOf(item):+0;-0;-}") { Align = Align.Center },
                        })
                        }
                    }
                ));                
            }
        }

        private dynamic getJsonFromApi(string request)
        {
            using (var req = new JsonWebRequest<dynamic>($"{base_url}/api/{request}"))
            {
                req.Perform();
                return req.ResponseObject;
            }
        }
    }
}
