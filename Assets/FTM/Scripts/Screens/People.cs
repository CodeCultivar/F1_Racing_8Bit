using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FTM
{
    public enum PersonKind { Driver, Engineer, Coordinator }

    /// <summary>Shared people table (Transfers tab + custom team setup) and full profile window.</summary>
    public static class People
    {
        public static readonly string[] DriverHeaders = { "NATIONALITY & NAME", "AGE", "OVR", "POT", "TEAM", "CONTRACT", "SIGNING BONUS", "SALARY", "" };
        public static readonly float[] DriverWidths = { 22, 5, 6, 6, 16, 8, 14, 15, 8 };
        public static readonly string[] EngineerHeaders = { "NATIONALITY & NAME", "AGE", "OVR", "TEAM", "SPECIALTY", "CONTRACT", "SIGNING BONUS", "SALARY", "" };
        public static readonly string[] CoordHeaders = { "NATIONALITY & NAME", "AGE", "OVR", "TEAM", "STYLE", "CONTRACT", "SIGNING BONUS", "SALARY", "" };
        public static readonly float[] StaffWidths = { 22, 5, 6, 16, 12, 8, 12, 11, 8 };

        public static string KindName(PersonKind k) { return k == PersonKind.Driver ? "Driver" : k == PersonKind.Engineer ? "Engineer" : "Coordinator"; }

        public static int[] StatsOf(PersonDef p)
        {
            var d = p as DriverDef; if (d != null) return d.stats;
            var e = p as EngineerDef; if (e != null) return e.stats;
            var c = p as CoordinatorDef; if (c != null) return c.stats;
            return new int[0];
        }

        public static string Contract(int seasons) { return seasons < 0 ? "--" : seasons + (seasons == 1 ? " YR" : " YRS"); }
        public static string Age(int a) { return a < 0 ? "--" : a.ToString(); }
        public static string NameLine(PersonDef p) { return (string.IsNullOrEmpty(p.nationality) ? "" : p.nationality.ToUpperInvariant() + "  ") + (string.IsNullOrEmpty(p.name) ? "(unnamed)" : p.name); }

        public static List<PersonDef> Pool(PersonKind k)
        {
            if (k == PersonKind.Driver) return Rules.Drivers.Cast<PersonDef>().ToList();
            if (k == PersonKind.Engineer) return Rules.Engineers.Cast<PersonDef>().ToList();
            return Rules.Coordinators.Cast<PersonDef>().ToList();
        }

        public static void BuildTable(Transform parent, float x, float y, float w, float h, PersonKind kind, List<PersonDef> people, CareerSave career, Action<PersonDef> onView, Func<PersonDef, bool> highlight = null)
        {
            string[] headers = kind == PersonKind.Driver ? DriverHeaders : kind == PersonKind.Engineer ? EngineerHeaders : CoordHeaders;
            float[] widths = kind == PersonKind.Driver ? DriverWidths : StaffWidths;
            var t = Table.Create(parent, x, y, w, h, headers, widths);
            if (people.Count == 0) { t.Message("No " + KindName(kind).ToLowerInvariant() + "s available. (" + Theme.NotConfigured + ")"); return; }

            foreach (var p in people)
            {
                bool hi = highlight != null && highlight(p);
                var row = t.AddRow(hi ? Theme.Darken(Theme.Red, 0.55f) : (Color?)null);
                int[] st = StatsOf(p);
                t.Cell(row, 0, NameLine(p), Theme.TextPrimary, true);
                t.Cell(row, 1, Age(p.age));
                t.Cell(row, 2, Rules.Fmt(Rules.Overall(st)), null, false, TextAnchor.MiddleRight);
                if (kind == PersonKind.Driver)
                {
                    var d = (DriverDef)p;
                    t.Cell(row, 3, Rules.Fmt(Rules.Overall(d.potential)), null, false, TextAnchor.MiddleRight);
                    t.Cell(row, 4, Rules.TeamNameOf(career, p), Theme.TextSecondary);
                }
                else
                {
                    t.Cell(row, 3, Rules.TeamNameOf(career, p), Theme.TextSecondary);
                    int b = Rules.BestIndex(st);
                    string spec = b < 0 ? "--" : (kind == PersonKind.Engineer ? Stat.Part[b] : Stat.Coord[b]);
                    t.Cell(row, 4, spec, Theme.TextSecondary);
                }
                t.Cell(row, 5, Contract(p.contractSeasons));
                t.Cell(row, 6, Rules.FmtMoney(p.signingBonus), p.signingBonus < 0 ? Theme.TextSecondary : Theme.Red, false, TextAnchor.MiddleRight);
                t.Cell(row, 7, p.salaryPerRace < 0 ? Theme.NotConfigured : Theme.Money(p.salaryPerRace) + " / RACE", p.salaryPerRace < 0 ? Theme.TextSecondary : Theme.Red, false, TextAnchor.MiddleRight);
                var person = p;
                Ui.Button(row, "VIEW", BtnStyle.Red, t.colX[8] + 8, 8, t.colW[8] - 16, 40, () => onView(person), 16);
            }
        }

        /// <summary>Profile body (all ratings). Returns the height used.</summary>
        public static float BuildProfile(Transform body, float w, PersonDef p, CareerSave career, Color team)
        {
            float y = 0;
            Ui.Icon(body, PixelArt.Portrait(team, ((p.id ?? "").GetHashCode() & 0x7fffffff)), 0, 0, 96, 96);
            Ui.Label(body, NameLine(p), Theme.PanelTitle, true, Theme.TextPrimary, 116, 0, w - 116, 32);
            Ui.Label(body, "Age " + Age(p.age) + "   •   " + Rules.TeamNameOf(career, p) + "   •   Contract " + Contract(p.contractSeasons), Theme.Small, false, Theme.TextSecondary, 116, 34, w - 116, 24);
            Ui.Label(body, "Salary " + (p.salaryPerRace < 0 ? Theme.NotConfigured : Theme.Money(p.salaryPerRace) + " / RACE") + "   •   Signing bonus " + Rules.FmtMoney(p.signingBonus), Theme.Small, false, Theme.TextSecondary, 116, 58, w - 116, 24);
            int[] st = StatsOf(p);
            var d = p as DriverDef;
            string ovr = "OVERALL " + Rules.Fmt(Rules.Overall(st)) + (d != null ? "    POTENTIAL " + Rules.Fmt(Rules.Overall(d.potential)) : "");
            Ui.Label(body, ovr, Theme.Body, true, Theme.Green, 116, 80, w - 116, 24);
            y = 120;

            string[] names = d != null ? Stat.Driver : (p is EngineerDef ? Stat.Part : Stat.Coord);
            for (int i = 0; i < names.Length; i++)
            {
                int v = Rules.StatAt(st, i);
                int pot = d != null ? Rules.StatAt(d.potential, i) : -1;
                Ui.Label(body, names[i], Theme.Body, false, Theme.TextPrimary, 0, y, 170, 40);
                Ui.Bar(body, 190, y + 14, w - 190 - 130, 12, Mathf.Max(0, v), Theme.Green, pot);
                string num = Rules.FmtInt(v) + (d != null ? " / " + Rules.FmtInt(pot) : "");
                Ui.Label(body, num, Theme.Body, true, Theme.TextPrimary, w - 120, y, 120, 40, TextAnchor.MiddleRight);
                y += 44;
            }
            return y;
        }

        public static float ProfileHeight(PersonDef p)
        {
            int n = p is DriverDef ? 5 : p is EngineerDef ? 6 : 3;
            return 120 + n * 44;
        }
    }
}
