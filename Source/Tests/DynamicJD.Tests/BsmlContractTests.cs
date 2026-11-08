using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DynamicJD.Tests
{
    /// <summary>
    /// Source-level BSML contract check. BSML binds by reflection and a typo only fails in-game, so
    /// this test verifies every value/action/formatter referenced by the markup exists on its host.
    /// </summary>
    public class BsmlContractTests
    {
        private static readonly (string Bsml, string Source)[] Files =
        {
            ("UI/Menu/mainMenu.bsml", "UI/Menu/MainMenuTab.cs"),
            ("UI/Menu/jdOverridePanel.bsml", "UI/Menu/JdOverrideMenuPanelController.cs"),
            ("UI/Clone/clone.bsml", "UI/Clone/CloneViewController.cs"),
            ("UI/Audio/audioMenu.bsml", "UI/Audio/AudioMenuTab.cs"),
        };

        private static readonly HashSet<string> Literals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "true", "false", "null", "Center", "Left", "Right", "Top", "Bottom", "None",
        };

        [Fact]
        public void EveryReferenceResolves()
        {
            string root = FindRoot();
            var problems = new List<string>();
            foreach ((string bsmlPath, string sourcePath) in Files)
            {
                string bsml = File.ReadAllText(Path.Combine(root, bsmlPath));
                string source = File.ReadAllText(Path.Combine(root, sourcePath));
                var members = new HashSet<string>(
                    Regex.Matches(source, @"\[UI(?:Value|Action|Component)\(\""([^\""]+)\""\)\]")
                        .Select(m => m.Groups[1].Value), StringComparer.Ordinal);

                foreach (string name in ExtractReferences(bsml))
                {
                    if (!members.Contains(name))
                    {
                        problems.Add(bsmlPath + ": '" + name + "' is referenced but not declared in " + sourcePath);
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void EmittedEventsAreDeclared()
        {
            string root = FindRoot();
            var problems = new List<string>();
            foreach ((string bsmlPath, string sourcePath) in Files)
            {
                string bsml = File.ReadAllText(Path.Combine(root, bsmlPath));
                var declared = new HashSet<string>(
                    Regex.Matches(bsml, @"\b(?:show|hide)-event\s*=\s*'([^']+)'")
                        .Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                foreach (Match m in Regex.Matches(bsml, @"\b(?:show|hide|click)-event\s*=\s*'([^']+)'|\bevent-click\s*=\s*'([^']+)'"))
                {
                    string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!declared.Contains(name))
                    {
                        problems.Add(bsmlPath + ": event '" + name + "' is emitted but no modal declares it");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        private static IEnumerable<string> ExtractReferences(string bsml)
        {
            foreach (Match m in Regex.Matches(bsml, @"(?:\bvalue|\bon-change|\bon-click|\bformatter)\s*=\s*'([^']+)'"))
            {
                string value = m.Groups[1].Value;
                if (value.StartsWith("~")) value = value.Substring(1);
                if (value.StartsWith("#")) continue;
                if (LiteralLike(value)) continue;
                if (value.Contains(" ")) continue;
                yield return value;
            }

            foreach (Match m in Regex.Matches(bsml, @"~([A-Za-z_][A-Za-z_0-9]*)"))
            {
                yield return m.Groups[1].Value;
            }
        }

        private static bool LiteralLike(string value)
        {
            if (Literals.Contains(value)) return true;
            if (value.Length == 0) return true;
            if (char.IsDigit(value[0])) return true;
            if (value.StartsWith("0x")) return true;
            return false;
        }

        private static string FindRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "DynamicJD.csproj")))
                {
                    return dir;
                }

                string nested = Path.Combine(dir, "DynamicJDPlugin");
                if (File.Exists(Path.Combine(nested, "DynamicJD.csproj")))
                {
                    return nested;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Could not locate DynamicJD.csproj from " + Directory.GetCurrentDirectory());
        }
    }
}
