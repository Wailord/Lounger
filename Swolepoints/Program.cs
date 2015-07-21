using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Swolepoints
{
    class Program
    {
        static void Main(string[] args)
        {
            const int DAYS_TO_CHECK = 90;
            //DateTime day_to_check = Convert.ToDateTime("5/25/2015");
            SwolepointsRanker ranks = new SwolepointsRanker(DateTime.Today.AddDays(-DAYS_TO_CHECK - 1));
            //Swolepoints ranks = new Swolepoints(DateTime.Today.AddDays(-150), DateTime.Today.AddDays(-7));
            Dictionary<string, HLTVTeam> ranked_teams;
            ranked_teams = ranks.GenerateTrueSkill(DateTime.Today.AddDays(-DAYS_TO_CHECK - 1), DateTime.Today.AddDays(1));
            List<HLTVTeam> teams = new List<HLTVTeam>();
            foreach (HLTVTeam t in ranked_teams.Values)
                teams.Add(t);

            //ranked_teams.RemoveAll(o => o.rating.StandardDeviation > 2.8);
            //ranked_teams.Sort((a, b) => b.elo.CompareTo(a.elo));
            //teams.Sort((a, b) => a.name.CompareTo(b.name));
            teams.Sort((a, b) => b.rating.ConservativeRating.CompareTo(a.rating.ConservativeRating));
            //ranked_teams.Sort((a, b) => b.rating.ConservativeRating.CompareTo(a.rating.ConservativeRating));

            using (StreamWriter w = new StreamWriter("./teams.json"))
            {
                w.WriteLine("Using knowlege of games from " + DateTime.Today.AddDays(1).Date.ToShortDateString() + " at the latest");
                foreach (HLTVTeam t in teams)
                {
                    w.WriteLine(t.name + ": " + Math.Round(t.rating.ConservativeRating * 50) + " (dev: " + t.rating.StandardDeviation + ")");
                }
            }

            ranks.GenerateWinLossChart();
        }
    }
}
