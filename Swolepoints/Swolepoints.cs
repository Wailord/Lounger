using System.IO;
using System.Text.RegularExpressions;
using System.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Moonmile;
using Moserware.Skills;
using HtmlAgilityPack;
using System.Threading.Tasks;

namespace Swolepoints
{
    enum SwolepointsOptions
    {

    };

    public class HLTVTeam
    {
        public string name;
        public double elo = 1000;
        public Rating rating = GameInfo.DefaultGameInfo.DefaultRating;
        public int wins;
        public int losses;
        public int ties;
        public int elo_games_played = 0;
        public Stack<DateTime> reset_dates = new Stack<DateTime>();

        public override string ToString()
        {
            return name;
        }
    }
    
    public class HLTVMatch
    {
        public int match_id;
        public HLTVTeam team_a;
        public HLTVTeam team_b;
        public int a_rounds;
        public int b_rounds;
        public DateTime date;

        public override string ToString()
        {
            return team_a.name + "(" + a_rounds + ") vs. " + team_b.name + " (" + b_rounds + ")";
        }
    }

    public class SwolepointsRanker
    {
        public Dictionary<string, HLTVTeam> Teams
        {
            get { return teams; }
        }
        Dictionary<string, HLTVTeam> teams = new Dictionary<string, HLTVTeam>();
        List<HLTVMatch> match_list = new List<HLTVMatch>();

        public SwolepointsRanker(DateTime start)
        {
            match_list = DownloadMatchData(start);
            Console.WriteLine("Parsed " + match_list.Count + " matches.");
        }

        private List<HLTVMatch> DownloadMatchData(DateTime start)
        {
            Uri current_url;
            List<HLTVMatch> matches = new List<HLTVMatch>();
            WebClient client;
            byte[] cur_data;
            string cur_page;
            int page_num = 0;
            string match_html;

            do
            {
                match_html = "http://www.hltv.org/?pageid=188&offset=" + page_num++ * 50;

                current_url = new Uri(match_html);
                client = new WebClient();
                cur_data = client.DownloadData(current_url);
                cur_page = Encoding.UTF8.GetString(cur_data);

                matches.AddRange(ParseHLTVPage(cur_page));
            }
            while (matches[matches.Count - 1].date >= start);

            matches.RemoveAll(o => o.date < start);
            matches.Reverse();

            return matches;
        }

        private List<HLTVMatch> ParseHLTVPage(string page)
        {
            List<HLTVMatch> page_matches = new List<HLTVMatch>();
            HtmlAgilityPack.HtmlDocument document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(page);

            var divs = document.DocumentNode.SelectNodes("//div[contains(@class,'covSmallHeadline')]");
            List<HtmlNode> div_list = new List<HtmlNode>();

            foreach (HtmlNode div in divs)
                div_list.Add(div);

            for (int x = 6; x < div_list.Count - 4; x += 5)
            {
                HLTVMatch m = new HLTVMatch();
                string date_string = div_list[x].InnerText;
                string match_id = div_list[x].ParentNode.Attributes[0].Value;
                match_id = match_id.Split('=')[2];

                date_string = Regex.Replace(
                    date_string,
                    "\\b(?<day>\\d{1,2})/(?<month>\\d{1,2}) (?<year>\\d{2,4})\\b",
                    "${month}/${day}/${year}", RegexOptions.None,
                    TimeSpan.FromMilliseconds(150));

                DateTime date = Convert.ToDateTime(date_string);
                DateTime.SpecifyKind(date, DateTimeKind.Utc);
                date = date.ToLocalTime();

                string[] team_arr = (div_list[x + 1].InnerText).Split('(');
                string team_a = team_arr[0].Substring(1, team_arr[0].Length - 2);
                int team_a_rounds = Convert.ToInt16(team_arr[1].Replace(")", ""));
                team_arr = (div_list[x + 2].InnerText).Split('(');
                string team_b = team_arr[0].Substring(1, team_arr[0].Length - 2);
                int team_b_rounds = Convert.ToInt16(team_arr[1].Replace(")", ""));
                string map = div_list[x + 3].InnerText;

                m.a_rounds = team_a_rounds;
                m.b_rounds = team_b_rounds;
                m.date = date;
                m.match_id = Convert.ToInt32(match_id);

                bool a_won = team_a_rounds > team_b_rounds;
                bool b_won = team_a_rounds < team_b_rounds;

                team_a = fixup(team_a);
                team_b = fixup(team_b);
                
                team_a = SwolepointsRanker.HLTVtoLounge(team_a);
                team_b = SwolepointsRanker.HLTVtoLounge(team_b);

                if (!teams.ContainsKey(team_a))
                teams.Add(team_a, new HLTVTeam{name = team_a,});
                
                m.team_a = teams[team_a];

                if (!teams.ContainsKey(team_b))
                    teams.Add(team_b, new HLTVTeam{name = team_b,});

                m.team_b = teams[team_b];
                if (a_won)
                {
                    teams[team_a].wins++;
                    teams[team_b].losses++;
                }
                else if (b_won)
                {
                    teams[team_b].wins++;
                    teams[team_a].losses++;
                }
                else
                {
                    teams[team_a].ties++;
                    teams[team_b].ties++;
                }
                page_matches.Add(m);
            }

            return page_matches;
        }

        public Dictionary<string, HLTVTeam> GenerateTrueSkill(DateTime start, DateTime end)
        {
            List<HLTVMatch> skill_matches = new List<HLTVMatch>(match_list);
            skill_matches.RemoveAll(o => o.date < start);
            skill_matches.RemoveAll(o => o.date > end);

            Console.WriteLine("Generating rankings between " + skill_matches[0].date.ToShortDateString() + " and " + skill_matches[skill_matches.Count - 1].date.ToShortDateString());
            
            foreach(HLTVTeam t in teams.Values)
            {
                t.rating = GameInfo.DefaultGameInfo.DefaultRating;
                t.rating = GameInfo.DefaultGameInfo.DefaultRating;
                t.reset_dates.Clear();
            }

            AddResetDates();

            foreach (HLTVMatch m in skill_matches)
            {
                if (reset_team_ranking(m.team_a, m.date))
                {
                    Console.WriteLine("Resetting " + m.team_a.name + " on " + m.date.ToShortDateString());
                    m.team_a.rating = GameInfo.DefaultGameInfo.DefaultRating;
                }

                if (reset_team_ranking(m.team_b, m.date))
                {
                    Console.WriteLine("Resetting " + m.team_b.name + " on " + m.date.ToShortDateString());
                    m.team_b.rating = GameInfo.DefaultGameInfo.DefaultRating;
                }

                var player1 = new Player(m.team_a);
                var player2 = new Player(m.team_b);
                var gameInfo = GameInfo.DefaultGameInfo;
                var team1 = new Moserware.Skills.Team(player1, m.team_a.rating);
                var team2 = new Moserware.Skills.Team(player2, m.team_b.rating);
                var ts_teams = Moserware.Skills.Teams.Concat(team1, team2);
                var newRatings = TrueSkillCalculator.CalculateNewRatings(gameInfo, ts_teams,
                    (m.a_rounds > m.b_rounds) ? 1 : 2,
                    (m.a_rounds > m.b_rounds) ? 2 : 1);

                m.team_a.rating = newRatings[player1];
                m.team_b.rating = newRatings[player2];

                teams[m.team_a.name].rating = newRatings[player1];
                teams[m.team_b.name].rating = newRatings[player2];
            }

            return teams;
        }

        struct winloss
        {
            public int wins;
            public int losses;
        };
        
        public void GenerateWinLossChart()
        {
            Dictionary<double, winloss> _winloss = new Dictionary<double, winloss>();
            int matches = 0;
            double round_to = 30.0;
            foreach (HLTVMatch m in match_list)
            {
                double skill_diff = m.team_a.rating.ConservativeRating - m.team_b.rating.ConservativeRating;
                skill_diff = Math.Floor(skill_diff * 50 / round_to) * round_to;

                if (!_winloss.ContainsKey(Math.Abs(skill_diff)))
                {
                    winloss wl = new winloss();
                    wl.wins = 0;
                    wl.losses = 0;
                    if (m.a_rounds > m.b_rounds)
                    {
                        // team a won
                        if (skill_diff > 0)
                        {
                            // add a win; add to the dictionary
                            // elo_diff is greater than zero
                            wl.wins++;
                            _winloss.Add(skill_diff, wl);
                        }
                        else
                        {
                            // add a loss; add to the dicionary
                            // elo_diff is less than zero
                            wl.losses++;
                            _winloss.Add(-skill_diff, wl);
                        }
                    }
                    else
                    {
                        if (skill_diff > 0)
                        {
                            // add a loss;
                            // elo_diff is greater than zero
                            wl.losses++;
                            _winloss.Add(skill_diff, wl);
                        }
                        else
                        {
                            // add a win;
                            // elo_diff is less than zero
                            wl.wins++;
                            _winloss.Add(-skill_diff, wl);
                        }
                    }
                }
                else if (m.a_rounds > m.b_rounds)
                {
                    if (skill_diff > 0)
                    {
                        winloss wl = _winloss[skill_diff];
                        wl.wins++;
                        _winloss[skill_diff] = wl;
                    }
                    else
                    {
                        winloss wl = _winloss[-skill_diff];
                        wl.losses++;
                        _winloss[-skill_diff] = wl;
                    }
                }
                else
                {
                    if (skill_diff > 0)
                    {
                        winloss wl = _winloss[skill_diff];
                        wl.losses++;
                        _winloss[skill_diff] = wl;
                    }
                    else
                    {
                        winloss wl = _winloss[-skill_diff];
                        wl.wins++;
                        _winloss[-skill_diff] = wl;
                    }
                }
                matches++;
            }

            using (StreamWriter r = new StreamWriter("../../winlossdata.txt"))
            {
                r.WriteLine("Diff" + "\t" + "W/L%");
                foreach (double i in _winloss.Keys)
                {
                    double wl_pct = Convert.ToDouble(_winloss[i].wins) / (_winloss[i].wins + _winloss[i].losses);
                    r.WriteLine(i.ToString() + "\t" + wl_pct.ToString());
                }
                r.WriteLine("Found" + matches + " matches");
            }
        }

        private string fixup(string team)
        {
            if (team == "Method") return "AffNity";
            if (team == "LDLC") return "Titan";
            if (team == "The Flying V") return "vVv";
            if (team == "CPH Wolves") return "exCPH";
            if (team == "undefined") return "exCPH";
            if (team == "k1ck") return "Alientech.Black";
            if (team == "Atlantis") return "Epsilon";

            return team;
        }

        public static string HLTVtoLounge(string hltv_name)
        {
            if (hltv_name == "PiTER") return "Piter";
            if (hltv_name == "Vega Squadron") return "Vega";
            if (hltv_name == "HellRaisers") return "HR";
            if (hltv_name == "Acer") return "TA";
            if (hltv_name == "LGB") return "LGB";
            if (hltv_name == "TSM") return "TSM";
            if (hltv_name == "ESC") return "ESC";
            if (hltv_name == "Epsilon") return "Epsilon";
            if (hltv_name == "CLG") return "CLG";
            if (hltv_name == "Keyd Stars") return "KeyD";
            if (hltv_name == "Liquid") return "Liquid";
            if (hltv_name == "Tempo Storm") return "TStorm";
            if (hltv_name == "Cloud9") return "Cloud9";
            if (hltv_name == "Luminosity") return "LG";
            if (hltv_name == "Nihilum") return "Nihilum";
            if (hltv_name == "ACE") return "AceG";
            if (hltv_name == "Gamers2") return "G2";
            if (hltv_name == "Virtus.pro") return "VP";
            if (hltv_name == "Property") return "Property";
            if (hltv_name == "Titan") return "Titan";
            if (hltv_name == "PENTA") return "Penta";
            if (hltv_name == "EnVyUs") return "EnVyUs";
            if (hltv_name == "LDLC Blue") return "LDLC.Blue";
            if (hltv_name == "KILLERFISH") return "KFish";
            if (hltv_name == "Orbit") return "Orbit";
            if (hltv_name == "dignitas") return "Dignitas";
            if (hltv_name == "Space Soldiers") return "SpaceS";
            if (hltv_name == "LDLC White") return "LDLC.White";
            if (hltv_name == "FlipSid3") return "FSid3";
            if (hltv_name == "NiP") return "NiP";
            if (hltv_name == "Natus Vincere") return "Na'Vi";
            if (hltv_name == "affNity") return "AffNity";
            if (hltv_name == "LunatiK") return "Lunatik";
            if (hltv_name == "eLevate") return "eLevate";
            if (hltv_name == "vVv") return "vVv";
            if (hltv_name == "vVv") return "vVv";
            if (hltv_name == "fnatic") return "Fnatic";
            if (hltv_name == "nerdRage") return "NR";
            if (hltv_name == "INSHOCK") return "INSHOCK";
            if (hltv_name == "mousesports") return "mouz";
            if (hltv_name == "GPlay") return "GPlay";
            if (hltv_name == "Moscow Five") return "M5";
            if (hltv_name == "TRICKED") return "Tricked";
            if (hltv_name == "Kinguin") return "Kinguin";
            if (hltv_name == "exCPH") return "CW";
            if (hltv_name == "nEophyte") return "nEophyte";
            if (hltv_name == "k1ck") return "K1CK";
            if (hltv_name == "CPLAY") return "CPlay";
            if (hltv_name == "Mia") return "MiA";
            if (hltv_name == "Jake Bube") return "JakeB";
            if (hltv_name == "SKDC") return "SKDC";
            if (hltv_name == "Tempo") return "Tempo";
            if (hltv_name == "mouseSpaz") return "mS";
            if (hltv_name == "HEADSHOTBG") return "HSBG";
            if (hltv_name == "ENCORE") return "ENCORE";
            if (hltv_name == "Rock") return "Rock";
            if (hltv_name == "x6tence") return "X6tence";
            if (hltv_name == "Immunity") return "Imm";
            if (hltv_name == "Vox Eminor") return "VOX";
            if (hltv_name == "Noble Honor") return "Noble";
            if (hltv_name == "7sway") return "7sway";
            if (hltv_name == "USSR") return "USSR";
            if (hltv_name == "Publiclir.se") return "Publiclir";
            if (hltv_name == "neXtPlease!") return "neXtP";
            if (hltv_name == "Airwalk") return "AW";
            if (hltv_name == "New Era") return "NEra";
            if (hltv_name == "ACES") return "Aces";
            if (hltv_name == "IGG") return "IGG";
            if (hltv_name == "AlienTech.Black") return "AT";
            if (hltv_name == "Wrecking") return "Wrecking";
            if (hltv_name == "CPH Wolves") return "CW";
            if (hltv_name == "Panthers") return "Panthers";
            if (hltv_name == "XPC") return "XPC";
            if (hltv_name == "iNation") return "Ination";
            if (hltv_name == "GIANT5") return "Giant5";
            if (hltv_name == "beGenius") return "beGenius";
            if (hltv_name == "RB") return "RB";
            if (hltv_name == "SYNRGY") return "SYNRGY";
            if (hltv_name == "Paradox") return "Paradox";
            if (hltv_name == "LAN DODGERS") return "LanD";
            if (hltv_name == "GreyFace") return "GFNS";
            if (hltv_name == "228") return "228";
            if (hltv_name == "Mostly Harmless") return "MostlyH";
            if (hltv_name == "UNLEASHED") return "UNL";
            if (hltv_name == "EZPZ_LS") return "EZPZ";
            if (hltv_name == "Sweden") return "Sweden";
            if (hltv_name == "Denmark") return "Denmark";
            if (hltv_name == "Norway") return "Norway";
            if (hltv_name == "Reason") return "Reason";
            if (hltv_name == "Divine") return "Divine";
            if (hltv_name == "Playing Ducks") return "PDucks";
            if (hltv_name == "myKPV.de") return "KPV";
            if (hltv_name == "fm-eSports") return "FM";
            if (hltv_name == "United Estonia") return "UE";
            if (hltv_name == "GLG") return "GLG";
            if (hltv_name == "Infused") return "Infused";
            if (hltv_name == "ENTiTY") return "Entity";
            if (hltv_name == "KnockOutStars") return "KOS";
            //if (hltv_name == "Atlantis") return "Epsilon";
            if (hltv_name == "H2B") return "H2B";
            if (hltv_name == "EYESports") return "EyeS";
            if (hltv_name == "xGame.KZ") return "xGame";
            if (hltv_name == "GGWP") return "GGWP";
            if (hltv_name == "Mythic") return "Mythic";
            if (hltv_name == "myRevenge") return "myRev";
            if (hltv_name == "volgare") return "Volgare";
            if (hltv_name == "The Flying V") return "vVv";
            if (hltv_name == "Flying V") return "vVv";
            if (hltv_name == "Fenix Fire") return "FxFire";
            if (hltv_name == "Xenex") return "Xenex";

            return hltv_name;
        }

        private bool reset_team_ranking(HLTVTeam team, DateTime cur)
        {
            if (team.reset_dates.Count == 0) return false;

            if (cur >= team.reset_dates.Peek().Date)
            {
                team.reset_dates.Pop();
                return true;
            }

            return false;
        }
        private void AddResetDates()
        {
            teams["Cloud9"].reset_dates.Push(Convert.ToDateTime("4/29/2015").Date);
            teams["LG"].reset_dates.Push(Convert.ToDateTime("4/29/2015").Date);
            teams["Nihilum"].reset_dates.Push(Convert.ToDateTime("4/30/2015").Date);
            teams["AceG"].reset_dates.Push(Convert.ToDateTime("5/17/2015").Date);
            teams["AceG"].reset_dates.Push(Convert.ToDateTime("5/13/2015").Date);
            teams["Dignitas"].reset_dates.Push(Convert.ToDateTime("1/25/2015").Date);
            teams["TSM"].reset_dates.Push(Convert.ToDateTime("1/25/2015").Date);
        }
    }
}
