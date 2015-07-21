using Swolepoints;
using System;
using System.Diagnostics;
using System.IO;
using SimpleJSON;
using System.Collections.Generic;
using System.Net;
using System.ComponentModel;
using System.Data;
using System.Xml.XPath;
using System.Net.Http;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing.Drawing2D;

namespace ezBet2
{
    public enum MatchFormat
    {
        BestOf1 = 1, BestOf2 = 2, BestOf3 = 3, BestOf5 = 5
    };

    public enum Winner
    {
        TeamA, TeamB, Tie, Canceled, NoWinner
    }

    public struct SimResults
    {
        public double start_amt;
        public double end_amt;
        public Match max_gain_match;
        public Match max_loss_match;
        public double max_gain;
        public double max_loss;
        public double net_pct_chg;
        public double peak_bankroll;
        public double lowest_bankroll;
        public int wins;
        public int losses;
    }

    public struct Match
    {
        public int match_id;
        public string match_event;
        public DateTime match_time;
        public bool closed;
        public HLTVTeam team_a;
        public double team_a_odds;
        public double team_b_odds;
        public double team_a_pct;
        public double team_b_pct;
        public HLTVTeam team_b;
        public Winner winner;
        public MatchFormat format;
        public int team_a_money;
        public int team_b_money;
        public bool rec_bet_a;
        public double rec_bet_pct;

        public override string ToString()
        {
            double ta = team_a_pct * 100;
            double tb = team_b_pct * 100;

            String singleMatch = String.Format(
                "{0,-25}{1,-5} vs {2,5}{3,25}",
                team_a.name + " (" + Math.Truncate(ta) + "%, " + Math.Truncate(team_a_odds * 100) / 100 + ")",
                " ",
                " ",
                team_b.name + " (" + Math.Truncate(tb) + "%, " + Math.Truncate(team_b_odds * 100) / 100 + ")"
                );

            return singleMatch;
        }
    }

    public partial class ezBet : Form
    {
        // TODO put in UI? dunno
        const string bankroll_path = @"..\..\simulations\bankroll.txt";

        public const int RANK_DAYS_BACK = 90;
        const double CSGL_MAX_BET = 300;

        private BindingSource bind = new BindingSource();
        private List<Match> all_time_matches = new List<Match>();
        private List<Match> current_matches = new List<Match>();
        private Dictionary<string, HLTVTeam> teams = new Dictionary<string, HLTVTeam>();
        private bool simShowing;

        struct winloss
        {
            public int wins;
            public int losses;
        };

        public ezBet()
        {
            new Thread(new ThreadStart(doSplash)).Start();

            InitializeComponent();
            update_rankings(DateTime.Today.AddDays(-RANK_DAYS_BACK), DateTime.Today.AddDays(1));
            all_time_matches = parse_matches();
            bind.DataSource = current_matches;
            GenerateWinLossChart();
            lst_matches.DataSource = bind;
            bind.ResetBindings(false);
            Cursor.Current = Cursors.Default;
            
            simShowing = true;
            hideSim();
            
            Application.Exit();
        }

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect, // x-coordinate of upper-left corner
            int nTopRect, // y-coordinate of upper-left corner
            int nRightRect, // x-coordinate of lower-right corner
            int nBottomRect, // y-coordinate of lower-right corner
            int nWidthEllipse, // height of ellipse
            int nHeightEllipse // width of ellipse
         );

        protected override void OnResize(EventArgs e)
        {
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 20, 20));
            base.OnResize(e);
        }

        public void doSplash()
        {
            SplashForm splash = new SplashForm();
            splash.Show();
            Application.Run();
        }

        private void GenerateWinLossChart()
        {
            Dictionary<double, winloss> _winloss = new Dictionary<double, winloss>();
            int matches = 0;
            foreach (Match m in all_time_matches)
            {
                //if (m.match_time < DateTime.Today.AddDays(-50)) continue;
                if (m.winner != Winner.TeamA && m.winner != Winner.TeamB) continue;
                double elo_diff = m.team_a.rating.ConservativeRating * 50 - m.team_b.rating.ConservativeRating * 50;
                elo_diff = Math.Floor(elo_diff / 50.0) * 50.0;

                if (!_winloss.ContainsKey(Math.Abs(elo_diff)))
                {
                    winloss wl = new winloss();
                    wl.wins = 0;
                    wl.losses = 0;
                    if (m.winner == Winner.TeamA)
                    {
                        if (elo_diff > 0)
                            wl.wins++;
                        else
                            wl.losses++;
                    }
                    else
                    {
                        if (elo_diff > 0)
                        {
                            wl.losses++;
                            _winloss.Add(elo_diff, wl);
                        }
                        else
                        {
                            wl.wins++;
                            _winloss.Add(-elo_diff, wl);
                        }
                    }
                }
                else if (m.winner == Winner.TeamA)
                {
                    if (elo_diff > 0)
                    {
                        winloss wl = _winloss[elo_diff];
                        wl.wins++;
                        _winloss[elo_diff] = wl;
                    }
                    else
                    {
                        winloss wl = _winloss[-elo_diff];
                        wl.losses++;
                        _winloss[-elo_diff] = wl;
                    }
                }
                else
                {
                    if (elo_diff > 0)
                    {
                        winloss wl = _winloss[elo_diff];
                        wl.losses++;
                        _winloss[elo_diff] = wl;
                    }
                    else
                    {
                        winloss wl = _winloss[-elo_diff];
                        wl.wins++;
                        _winloss[-elo_diff] = wl;
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
        private bool fits_criteria(Match match, HLTVTeam team_a, HLTVTeam team_b)
        {
            if (team_a.rating.StandardDeviation > 2.2 || team_b.rating.StandardDeviation > 2.2) return false;
            return true;
        }
        private SimResults SimulateAllParsedMatches(DateTime sim_start, DateTime sim_end,
            double start_bank, string path, bool limit_max, double max_pct, bool make_file)
        {
            SimResults results = new SimResults();
            Dictionary<DateTime, List<Match>> date_map = new Dictionary<DateTime, List<Match>>();
            double daily_bankroll = 0.0f;
            results.start_amt = start_bank;
            results.end_amt = start_bank;
            results.max_gain = -1.0f;
            results.max_loss = -1.0f;
            results.lowest_bankroll = start_bank;
            results.peak_bankroll = start_bank;
            results.wins = 0;
            results.losses = 0;

            foreach (Match m in all_time_matches)
            {
                if (!date_map.ContainsKey(m.match_time.Date))
                    date_map.Add(m.match_time.Date, new List<Match>());

                date_map[m.match_time.Date].Add(m);
            }

            System.IO.StreamWriter file = null;
            if (make_file)
            {
                file = new System.IO.StreamWriter(path, false);
                file.WriteLine(
                    "MatchID" + "\t"
                    + "Date" + "\t"
                    + "Winner" + "\t"
                    + "WnrSwlPts" + "\t"
                    + "WOdds" + "\t"
                    + "Loser" + "\t"
                    + "LsrSwlPts" + "\t"
                    + "LOdds" + "\t"
                    + "Pre-BR" + "\t"
                    + "BetAmt" + "\t"
                    + "NetChg" + "\t"
                    + "%Chg" + "\t"
                    + "Post-BR");
                file.WriteLine(
                    "0000" + "\t" // match id
                    + "00/00/0000" + "\t" // date
                    + "" + "\t" // winner
                    + "0000" + "\t" // winner swolepoints
                    + "0.00" + "\t" // winner odds
                    + "" + "\t" // loser
                    + "0000" + "\t" // loser swolepoints
                    + "0.00" + "\t" // loser odds
                    + results.start_amt + "\t" // pre-br
                    + "0.00" + "\t" // bet amt
                    + "0.00" + "\t" // netchg
                    + "0.00%" + "\t" // %chg
                    + results.end_amt
                ); // post-br
            }
            int skip_games = 0;
            DateTime old_date = DateTime.MinValue;
            DateTime cur_date = DateTime.MinValue;
            SwolepointsRanker ranker = new SwolepointsRanker(sim_start.AddDays(-RANK_DAYS_BACK));
            Dictionary<string, HLTVTeam> rank_teams = null;
            foreach (DateTime day in date_map.Keys)
            {
                if (day < sim_start || day > sim_end.AddMinutes(1440)) continue;
                //daily_bankroll = Math.Truncate(results.end_amt / date_map[day].Count * 100) / 100;
                daily_bankroll = Math.Truncate(results.end_amt / 10 * 100) / 100;
                
                foreach (Match match in date_map[day])
                {
                    if (match.winner == Winner.NoWinner) continue;

                    if (rank_teams != null && !fits_criteria(match, rank_teams[match.team_a.name], rank_teams[match.team_b.name]))
                    {
                        skip_games++;
                        continue;
                    }

                    cur_date = match.match_time.Date;
                    if (old_date == DateTime.MinValue || cur_date > old_date)
                    {
                        rank_teams = ranker.GenerateTrueSkill(cur_date.AddDays(-RANK_DAYS_BACK), cur_date.AddDays(-1));
                        old_date = cur_date;
                    }

                    if (rank_teams != null && !fits_criteria(match, rank_teams[match.team_a.name], rank_teams[match.team_b.name]))
                    {
                        skip_games++;
                        continue;
                    }

                    bool team_a_favored;
                    double smart_win_chance;
                    bool team_a_smart;
                    double win_probability = get_win_probability(
                        rank_teams[match.team_a.name].rating,
                        rank_teams[match.team_b.name].rating,
                        match.format,
                        out team_a_favored);
                    double rec_bet_pct;

                    if (team_a_favored)
                        rec_bet_pct = get_recommended_bet_pct(win_probability, match.team_a_odds);
                    else
                        rec_bet_pct = get_recommended_bet_pct(win_probability, match.team_b_odds);

                    if (team_a_favored && rec_bet_pct < 0)
                    {
                        // team A has a higher chance of winning, but less of a chance than Lounge says;
                        // therefore, team B is the smarter bet
                        smart_win_chance = (1 - win_probability);
                        team_a_smart = false;
                        rec_bet_pct = get_recommended_bet_pct(smart_win_chance, match.team_b_odds);
                    }
                    else if (team_a_favored && rec_bet_pct >= 0)
                    {
                        // team A has a higher chance of winning by an amount greater than Lounge says;
                        // therefore, team A is still the smarter bet
                        smart_win_chance = win_probability;
                        team_a_smart = true;
                        rec_bet_pct = get_recommended_bet_pct(smart_win_chance, match.team_a_odds);
                    }
                    else if (!team_a_favored && rec_bet_pct < 0)
                    {
                        // team B has a higher chance of winning, but less of a chance than Lounge says;
                        // therefore, team A is the smarter bet
                        smart_win_chance = (1 - win_probability);
                        team_a_smart = true;
                        rec_bet_pct = get_recommended_bet_pct(smart_win_chance, match.team_a_odds);
                    }
                    else
                    {
                        // team B has a higher chance of winning by an amount greater than Lounge says;
                        // therefore, team B is still the smarter bet
                        smart_win_chance = win_probability;
                        team_a_smart = false;
                        rec_bet_pct = get_recommended_bet_pct(smart_win_chance, match.team_b_odds);
                    }

                    double match_change = 0.0f;
                    double bet_amt = 0.0f;
                    if (results.end_amt > 0.03)
                    {
                        if (team_a_smart && match.winner == Winner.TeamA)
                        {
                            // team A won and we bet on it
                            results.wins++;
                            bet_amt = rec_bet_pct * daily_bankroll;
                            if (limit_max)
                                bet_amt = Math.Min(CSGL_MAX_BET, bet_amt);
                            bet_amt = Math.Min(bet_amt, daily_bankroll / max_pct);
                            bet_amt = Math.Truncate(bet_amt * 100) / 100;
                            match_change = Math.Truncate(bet_amt * match.team_a_odds * 100) / 100;
                            if (results.max_gain == -1.0f || results.max_gain < match_change)
                            {
                                results.max_gain = match_change;
                                results.max_gain_match = match;
                            }
                            if (make_file)
                                file.WriteLine(
                                    match.match_id + "\t"
                                    + match.match_time.ToShortDateString() + "\t"
                                    + match.team_a.name + "\t"
                                    + Math.Round(teams[match.team_a.name].rating.ConservativeRating * 50) + "\t"
                                    + match.team_b_pct + "\t"
                                    + match.team_b.name + "\t"
                                    + Math.Round(teams[match.team_b.name].rating.ConservativeRating * 50) + "\t"
                                    + match.team_a_pct + "\t"
                                    + results.end_amt + "\t"
                                    + bet_amt + "\t"
                                    + match_change + "\t"
                                    + Math.Truncate((match_change / results.end_amt) * 100) / 100 + "\t"
                                    + (results.end_amt + match_change));
                            results.end_amt += match_change;
                            if (results.end_amt > results.peak_bankroll) results.peak_bankroll = results.end_amt;
                        }
                        else if (!team_a_smart && match.winner == Winner.TeamB)
                        {
                            // team B won and we bet on it
                            results.wins++;
                            bet_amt = rec_bet_pct * daily_bankroll;
                            if (limit_max)
                                bet_amt = Math.Min(CSGL_MAX_BET, bet_amt);
                            bet_amt = Math.Min(bet_amt, daily_bankroll / max_pct);
                            bet_amt = Math.Truncate(bet_amt * 100) / 100;
                            match_change = Math.Truncate(bet_amt * match.team_b_odds * 100) / 100;
                            if (results.max_gain == -1.0f || results.max_gain < match_change)
                            {
                                results.max_gain = match_change;
                                results.max_gain_match = match;
                            }
                            if (make_file)
                                file.WriteLine(
                                    match.match_id + "\t"
                                    + match.match_time.ToShortDateString() + "\t"
                                    + match.team_b.name + "\t"
                                    + Math.Round(rank_teams[match.team_b.name].rating.ConservativeRating * 50) + "\t"
                                    + match.team_b_pct + "\t"
                                    + match.team_a.name + "\t"
                                    + Math.Round(rank_teams[match.team_a.name].rating.ConservativeRating * 50) + "\t"
                                    + match.team_a_pct + "\t"
                                    + results.end_amt + "\t"
                                    + bet_amt + "\t"
                                    + match_change + "\t"
                                    + Math.Truncate((match_change / results.end_amt) * 100) / 100 + "\t"
                                    + (results.end_amt + match_change)
                            );
                            results.end_amt += match_change;
                            if (results.end_amt > results.peak_bankroll) results.peak_bankroll = results.end_amt;
                        }
                        else if (team_a_smart && match.winner == Winner.TeamB || !team_a_smart && match.winner == Winner.TeamA)
                        {
                            // we lost
                            results.losses++;
                            bet_amt = rec_bet_pct * daily_bankroll;
                            if (limit_max)
                                bet_amt = Math.Min(CSGL_MAX_BET, bet_amt);
                            bet_amt = Math.Min(bet_amt, daily_bankroll / max_pct);
                            bet_amt = Math.Truncate(bet_amt * 100) / 100;
                            match_change = Math.Truncate(bet_amt * 100) / 100;
                            if (results.max_loss == -1.0f || results.max_loss < match_change)
                            {
                                results.max_loss = match_change;
                                results.max_loss_match = match;
                            }
                            if (make_file)
                                file.WriteLine(
                                    match.match_id + "\t"
                                    + match.match_time.ToShortDateString() + "\t"
                                    + (match.rec_bet_a ? match.team_b.name : match.team_a.name) + "\t"
                                    + (match.rec_bet_a ?
                                    Math.Round(rank_teams[match.team_b.name].rating.ConservativeRating * 50)
                                    : Math.Round(rank_teams[match.team_a.name].rating.ConservativeRating * 50)) + "\t"
                                    + (match.rec_bet_a ? match.team_b_pct : match.team_a_pct) + "\t"
                                    + (match.rec_bet_a ? match.team_a.name : match.team_b.name) + "\t"
                                    + (match.rec_bet_a ?
                                    Math.Round(rank_teams[match.team_a.name].rating.ConservativeRating * 50)
                                    : Math.Round(rank_teams[match.team_b.name].rating.ConservativeRating * 50)) + "\t"
                                    + (match.rec_bet_a ? match.team_a_pct : match.team_b_pct) + "\t"
                                    + results.end_amt + "\t"
                                    + bet_amt + "\t"
                                    + -match_change + "\t"
                                    + Math.Truncate((-match_change / results.end_amt) * 100) / 100 + "\t"
                                    + (results.end_amt - match_change)
                                    );
                            results.end_amt -= match_change;
                            if (results.end_amt < results.lowest_bankroll) results.lowest_bankroll = results.end_amt;
                        }
                        results.end_amt = Math.Truncate(results.end_amt * 100) / 100;
                    }
                    else
                        break;
                }
            }
            if (make_file)
                file.Dispose();

            results.net_pct_chg = results.end_amt / results.start_amt - 1;

            return results;
        }
        private void btn_ez_Click(object sender, EventArgs e)
        {
            all_time_matches = parse_matches();
            bind.ResetBindings(false);
        }
        double get_bo2_chance(double one_map_chance)
        {
            // assumes 50% chance of winning between two even teams
            return one_map_chance * one_map_chance + (1 - one_map_chance) * one_map_chance;
        }
        double get_bo3_chance(double one_map_chance)
        {
            return (3 * one_map_chance * one_map_chance) - (2 * one_map_chance * one_map_chance * one_map_chance);
        }
        double get_bo5_chance(double x)
        {
            double chance = Math.Pow(x, 3) * (1 - x) * (5 + (1 - x) + (1 - x) + (1 - x) + (1 - x) + (1 - x) + (1 - x));

            return chance;
        }
        private List<Match> parse_matches()
        {
            int num_matches = 0;
            List<Match> parsed_matches;
            string matches_stats;
            Uri url;
            WebClient client;
            byte[] data;
            JSONNode full_matches;
            string matches;

            all_time_matches.Clear();
            current_matches.Clear();

            try
            {
                url = new Uri("http://www.csgolounge.com/api/matches");
                client = new WebClient();
                data = client.DownloadData(url);
                matches = Encoding.UTF8.GetString(data);

                url = new Uri("http://www.csgolounge.com/api/matches_stats");
                data = client.DownloadData(url);
                matches_stats = Encoding.UTF8.GetString(data);

                using (StreamWriter w = new StreamWriter("../../matches.json"))
                {
                    w.Write(matches);
                }
                using (StreamWriter w = new StreamWriter("../../matches_stats.json"))
                {
                    w.Write(matches_stats);
                }
            }
            catch (Exception)
            {
                using (StreamReader r = new StreamReader("../../matches.json"))
                {
                    matches = r.ReadToEnd();
                };
                using (StreamReader r = new StreamReader("../../matches_stats.json"))
                {
                    matches_stats = r.ReadToEnd();
                };
                MessageBox.Show("Could not connect to CSGOLounge; using last saved copy");
            }

            full_matches = SimpleJSON.JSON.Parse(matches);
            num_matches = full_matches.Count;
            parsed_matches = new List<Match>();

            Dictionary<int, int> bet_id_map = new Dictionary<int, int>();
            JSONNode match_bets = SimpleJSON.JSON.Parse(matches_stats);
            for (int x = 0; x < match_bets.Count; x++)
                bet_id_map.Add(match_bets[x]["match"].AsInt, x);

            for (int x = 0; x < num_matches; x++)
            {
                bool closed = (full_matches[x]["closed"].AsInt == 1);
                int match_id = full_matches[x]["match"].AsInt;
                if (!bet_id_map.ContainsKey(match_id)) continue;

                string match_event = full_matches[x]["event"].Value;
                DateTime match_time = Convert.ToDateTime(full_matches[x]["when"].Value);
                DateTime.SpecifyKind(match_time, DateTimeKind.Utc);
                match_time = match_time.ToLocalTime();
                string team1_text = full_matches[x]["a"].Value;
                string team2_text = full_matches[x]["b"].Value;
                string s_winner = full_matches[x]["winner"].Value;
                int team_a_money = match_bets[bet_id_map[match_id]]["a"].AsInt;
                int team_b_money = match_bets[bet_id_map[match_id]]["b"].AsInt;
                double team_a_odds = Convert.ToDouble(team_b_money) / Convert.ToDouble(team_a_money);
                double team_b_odds = Convert.ToDouble(team_a_money) / Convert.ToDouble(team_b_money);
                double smart_win_chance = 0.0f;
                bool team_a_smart = false;

                MatchFormat format = (MatchFormat)full_matches[x]["format"].AsInt;

                Winner winner;
                switch (s_winner)
                {
                    case "a":
                        winner = Winner.TeamA;
                        break;
                    case "b":
                        winner = Winner.TeamB;
                        break;
                    case "c":
                        winner = Winner.Tie;
                        break;
                    default:
                        winner = Winner.NoWinner;
                        break;
                }

                bool team_a_favored = true;

                if (!teams.ContainsKey(team1_text))
                {
                    /*
                    if(match_time > DateTime.Today.AddDays(-45))
                    MessageBox.Show(team1_text);
                    */
                    continue;
                }
                if (!teams.ContainsKey(team2_text))
                {
                    /*
                    if (match_time > DateTime.Today.AddDays(-45))
                    MessageBox.Show(team2_text);
                    */
                    continue;
                }

                double win_probability = get_win_probability(
                    teams[team1_text].rating,
                    teams[team2_text].rating,
                    format,
                    out team_a_favored);
                double rec_bet_pct;

                if (team_a_favored)
                    rec_bet_pct = get_recommended_bet_pct(win_probability, team_a_odds);
                else
                    rec_bet_pct = get_recommended_bet_pct(win_probability, team_b_odds);

                if (team_a_favored && rec_bet_pct < 0)
                {
                    // team A has a higher chance of winning, but less of a chance than Lounge says;
                    // therefore, team B is the smarter bet
                    smart_win_chance = (1 - win_probability);
                    team_a_smart = false;
                    rec_bet_pct = get_recommended_bet_pct(smart_win_chance, team_b_odds);
                }
                else if (team_a_favored && rec_bet_pct >= 0)
                {
                    // team A has a higher chance of winning by an amount greater than Lounge says;
                    // therefore, team A is still the smarter bet
                    smart_win_chance = win_probability;
                    team_a_smart = true;
                    rec_bet_pct = get_recommended_bet_pct(smart_win_chance, team_a_odds);
                }
                else if (!team_a_favored && rec_bet_pct < 0)
                {
                    // team B has a higher chance of winning, but less of a chance than Lounge says;
                    // therefore, team A is the smarter bet
                    smart_win_chance = (1 - win_probability);
                    team_a_smart = true;
                    rec_bet_pct = get_recommended_bet_pct(smart_win_chance, team_a_odds);
                }
                else
                {
                    // team B has a higher chance of winning by an amount greater than Lounge says;
                    // therefore, team B is still the smarter bet
                    smart_win_chance = win_probability;
                    team_a_smart = false;
                    rec_bet_pct = get_recommended_bet_pct(smart_win_chance, team_b_odds);
                }

                Match new_match = new Match
                {
                    match_id = match_id,
                    match_event = match_event,
                    match_time = match_time,
                    team_a = teams[team1_text],
                    closed = closed,
                    team_b = teams[team2_text],
                    winner = winner,
                    format = format,
                    team_a_money = team_a_money,
                    team_b_money = team_b_money,
                    team_a_odds = team_a_odds * .99,
                    team_b_odds = team_b_odds * .99,
                    team_a_pct = team_a_money / Convert.ToDouble(team_a_money + team_b_money),
                    team_b_pct = team_b_money / Convert.ToDouble(team_a_money + team_b_money),
                    rec_bet_a = team_a_smart,
                    rec_bet_pct = rec_bet_pct,
                };

                if (!new_match.closed && fits_criteria(new_match, new_match.team_a, new_match.team_b)) current_matches.Add(new_match);
                parsed_matches.Add(new_match);
            }

            parsed_matches.Sort((e, z) => e.match_time.CompareTo(z.match_time));
            current_matches.Sort((e, z) => e.match_time.CompareTo(z.match_time));

            return parsed_matches;
        }

        private void update_rankings(DateTime start, DateTime end)
        {
            teams.Clear();
            SwolepointsRanker ranker = new SwolepointsRanker(start);
            teams = ranker.GenerateTrueSkill(start, end);
        }

        private double get_win_probability(Moserware.Skills.Rating team_a_rating, Moserware.Skills.Rating team_b_rating, MatchFormat format, out bool team_a_favored)
        {
            double chance;
            double elo_diff;
            team_a_favored = (team_a_rating.ConservativeRating > team_b_rating.ConservativeRating);
            elo_diff = Math.Abs(team_a_rating.ConservativeRating - team_b_rating.ConservativeRating);

            // nuts rankings
            //chance = 1 - 1 / (1 + Math.Exp(0.00583 * elo_diff));
            //chance = 0.0007 * elo_diff + 0.5;
            //chance = .5 + elo_diff * 0.001;
            //chance = 0.0013 * elo_diff + 0.5;

            // swolepoints
            // old
            //chance = 0.2709 * Math.Pow(elo_diff * 100, 0.1861);
            // new
            //chance = .269 * Math.Pow(50 * elo_diff, 0.184);
            // higher r2 val
            //chance = 2 * Math.Pow(10, -10) * Math.Pow(elo_diff * 50, 3) - (7 * Math.Pow(10, -7) * Math.Pow(elo_diff * 50, 2)) + .0001 * (elo_diff * 50) + 0.5;
            // low r2, seems to get $$$
            chance = 0.0005 * elo_diff * 50 + 0.5;
            //chance = 0.2565 * Math.Pow(50 * elo_diff, 0.1951);

            // ts formula
            //chance = -4 * Math.Pow(10, -7) * Math.Pow(elo_diff * 50, 2) + .0009 * elo_diff * 50 + 0.5;
            //chance = .0011 * elo_diff * 50 + 0.5;

            if (format == MatchFormat.BestOf2)
                return get_bo2_chance(chance);
            else if (format == MatchFormat.BestOf3)
                return get_bo3_chance(chance);
            else if (format == MatchFormat.BestOf5)
                return get_bo5_chance(chance);
            else
                return chance;
        }
        private double get_recommended_bet_pct(double win_chance, double csgl_odds)
        {
            return (win_chance * (csgl_odds + 1) - 1) / csgl_odds;
        }
        private void lst_matches_SelectedIndexChanged(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            DisplayMatch(lst_matches.SelectedIndex);
            Cursor.Current = Cursors.Default;
        }
        private void DisplayMatch(int match_index)
        {
            Match selected_match;
            double bankroll = Convert.ToDouble(updn_bankroll.Text);
            double payout = 0.0f;
            double rec_bet_amt = 0.0f;
            double rec_bet_pct = 0.0f;

            if (match_index != -1)
            {
                selected_match = current_matches[match_index];

                // calculate win probability for better team
                bool team_a_favored = true;
                double win_probability = get_win_probability(
                    teams[selected_match.team_a.name].rating,
                    teams[selected_match.team_b.name].rating,
                    selected_match.format,
                    out team_a_favored);

                if (team_a_favored)
                {
                    // team A has a higher chance of winning the match
                    txt_favorite.Text = selected_match.team_a.name;
                    txt_underdog.Text = selected_match.team_b.name;

                    if (!selected_match.rec_bet_a)
                    {
                        // team B is the smarter bet
                        txt_smart_bet.Text = selected_match.team_b.name;
                        txt_smart_win_chance.Text = Math.Truncate((1 - win_probability) * 100) + "%";
                        txt_lounge_odds.Text = Math.Truncate(selected_match.team_b_pct * 100) + "%";
                        rec_bet_pct = get_recommended_bet_pct(1 - win_probability, selected_match.team_b_odds);
                        rec_bet_amt = Math.Min(Math.Min(CSGL_MAX_BET, selected_match.rec_bet_pct * (bankroll / 10)), bankroll / 2);
                        rec_bet_amt = Math.Truncate(rec_bet_amt * 100) / 100;
                        payout = rec_bet_amt * selected_match.team_b_odds;
                        payout = Math.Truncate(payout * 100) / 100 + rec_bet_amt;


                        lbl_bet_amt.Text = "$" + rec_bet_amt.ToString();
                        lbl_bet_on.Text = selected_match.team_b.name;
                        lbl_payout.Text = "$" + payout.ToString();
                        lbl_profit.Text = "$" + (payout - rec_bet_amt).ToString();
                    }
                    else
                    {
                        // team A is still the smarter bet
                        txt_smart_bet.Text = selected_match.team_a.name;
                        txt_smart_win_chance.Text = Math.Truncate(win_probability * 100) + "%";
                        txt_lounge_odds.Text = Math.Truncate(selected_match.team_a_pct * 100) + "%";
                        rec_bet_amt = Math.Min(Math.Min(CSGL_MAX_BET, selected_match.rec_bet_pct * (bankroll / 10)), bankroll / 2);
                        rec_bet_amt = Math.Truncate(rec_bet_amt * 100) / 100;
                        payout = rec_bet_amt * selected_match.team_a_odds;
                        payout = Math.Truncate(payout * 100) / 100 + rec_bet_amt;

                        lbl_bet_amt.Text = "$" + rec_bet_amt.ToString();
                        lbl_bet_on.Text = selected_match.team_a.name;
                        lbl_payout.Text = "$" + payout.ToString();
                        lbl_profit.Text = "$" + (payout - rec_bet_amt).ToString();
                    }
                }
                else
                {
                    // team B has a higher chance of winning the match
                    txt_favorite.Text = selected_match.team_b.name;
                    txt_underdog.Text = selected_match.team_a.name;

                    rec_bet_pct = get_recommended_bet_pct(win_probability, selected_match.team_b_odds);
                    if (rec_bet_pct < 0)
                    {
                        // team A is the smarter bet
                        txt_smart_bet.Text = selected_match.team_a.name;
                        txt_smart_win_chance.Text = Math.Truncate((1 - win_probability) * 100) + "%";
                        txt_lounge_odds.Text = Math.Truncate(selected_match.team_a_pct * 100) + "%";
                        rec_bet_amt = Math.Min(Math.Min(CSGL_MAX_BET, selected_match.rec_bet_pct * (bankroll / 10)), bankroll / 2);
                        rec_bet_amt = Math.Truncate(rec_bet_amt * 100) / 100;
                        payout = rec_bet_amt * selected_match.team_a_odds;
                        payout = Math.Truncate(payout * 100) / 100 + rec_bet_amt;

                        lbl_bet_amt.Text = "$" + rec_bet_amt.ToString();
                        lbl_bet_on.Text = selected_match.team_a.name;
                        lbl_payout.Text = "$" + payout.ToString();
                        lbl_profit.Text = "$" + (payout - rec_bet_amt).ToString();
                    }
                    else
                    {
                        // team B is still the smarter bet
                        txt_smart_bet.Text = selected_match.team_b.name;
                        txt_smart_win_chance.Text = Math.Truncate(win_probability * 100) + "%";
                        txt_lounge_odds.Text = Math.Truncate(selected_match.team_b_pct * 100) + "%";
                        rec_bet_amt = Math.Min(Math.Min(CSGL_MAX_BET, selected_match.rec_bet_pct * (bankroll / 10)), bankroll / 2);
                        rec_bet_amt = Math.Truncate(rec_bet_amt * 100) / 100;
                        payout = rec_bet_amt * selected_match.team_b_odds;
                        payout = Math.Truncate(payout * 100) / 100 + rec_bet_amt;

                        lbl_bet_amt.Text = "$" + rec_bet_amt.ToString();
                        lbl_bet_on.Text = selected_match.team_b.name;
                        lbl_payout.Text = "$" + payout.ToString();
                        lbl_profit.Text = "$" + (payout - rec_bet_amt).ToString();
                    }
                }
            }
        }
        private void txt_bankroll_KeyUp(object sender, KeyEventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            DisplayMatch(lst_matches.SelectedIndex);
            Cursor.Current = Cursors.Default;
        }
        private void btn_gensim_Click(object sender, EventArgs e)
        {
            SimResults results = SimulateAllParsedMatches(dt_sim_start.Value, dt_sim_end.Value,
                Convert.ToDouble(updn_sim_bankroll.Value), bankroll_path,
                chk_use_max.Checked, Convert.ToDouble(100 / sld_bet_pct.Value), chk_gen_file.Checked);

            if (chk_gen_file.Checked)
                Process.Start(bankroll_path);

            if (results.wins == 0 && results.losses == 0)
            {
                lbl_sim_start_amt.Text = String.Empty;
                lbl_sim_end_amt.Text = String.Empty;
                lbl_sim_net_chg.Text = String.Empty;
                lbl_sim_biggest_loss.Text = String.Empty;
                lbl_sim_biggest_win.Text = String.Empty;
                lbl_sim_lowest.Text = String.Empty;
                lbl_sim_peak.Text = String.Empty;
                lbl_win_pct.Text = String.Empty;
            }
            else
            {
                double profit = (Math.Truncate(100 * (results.end_amt - results.start_amt)) / 100);
                string profit_string = String.Empty;
                if (profit >= 0) profit_string += "+$";
                else profit_string += "-$";

                double win_pct = Convert.ToDouble(results.wins) / (results.wins + results.losses);
                win_pct = Math.Truncate(10000 * win_pct) / 100;
                string win_pct_string = win_pct.ToString() + "%";

                lbl_sim_start_amt.Text = "$" + (Math.Truncate(100 * updn_sim_bankroll.Value) / 100).ToString();
                lbl_sim_end_amt.Text = "$" + (Math.Truncate(100 * results.end_amt) / 100).ToString();
                lbl_sim_net_chg.Text = profit_string + Math.Abs(profit);
                lbl_sim_biggest_loss.Text = "-$" + (Math.Truncate(100 * results.max_loss) / 100).ToString();
                lbl_sim_biggest_win.Text = "$" + (Math.Truncate(100 * results.max_gain) / 100).ToString();
                lbl_sim_lowest.Text = "$" + (Math.Truncate(100 * results.lowest_bankroll) / 100).ToString();
                lbl_sim_peak.Text = "$" + (Math.Truncate(100 * results.peak_bankroll) / 100).ToString();
                lbl_win_pct.Text = win_pct_string;
            }
        }

        private void sld_bet_pct_Scroll(object sender, EventArgs e)
        {
            tip_bet_pct_amt.SetToolTip(sld_bet_pct, sld_bet_pct.Value.ToString());
        }

        private void lbl_sim_start_Click(object sender, EventArgs e)
        {
            dt_sim_start.Value = DateTime.Today;
        }

        private void lbl_sim_end_Click(object sender, EventArgs e)
        {
            dt_sim_end.Value = DateTime.Today;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        const int HT_CAPTION = 0x2;
        const int WM_NCLBUTTONDOWN = 0xA1;
        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();

        private void ezBet_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void roundedButton1_Click(object sender, EventArgs e)
        {
            hideSim();
        }

        private void hideSim()
        {
            if(simShowing)
            {
                Width -= 206;
                pnl_hidden_background.Width -= 206;
                simShowing = false;
                btnHideSim.BackgroundImage = ezBet2.Properties.Resources.ChevronRight;
                pnlSimulation.Hide();
            }
            else
            {
                Width += 206;
                pnl_hidden_background.Width += 206;
                simShowing = true;
                btnHideSim.BackgroundImage = ezBet2.Properties.Resources.ChevronLeft;
                pnlSimulation.Show();
            }
        }

        private void btnMinimize_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }
    }
}