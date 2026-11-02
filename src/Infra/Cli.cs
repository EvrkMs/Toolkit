using System;
using System.Collections.Generic;

namespace Toolkit
{
    internal sealed class Cli
    {
        private readonly Dictionary<string, string> _options =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _flags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Verb = "";
        public string SubVerb = "";

        public static readonly string[] ValueOptions = new[]
        {
            "--config", "--explorer-restart", "--root", "--source", "--dest",
            "--recreate", "--include-programdata", "--include-program-files",
            "--resolution", "--profile"
        };

        public static Cli Parse(string[] args)
        {
            Cli cli = new Cli();
            List<string> positional = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrWhiteSpace(a)) continue;

                if (a.StartsWith("--", StringComparison.Ordinal) || a.StartsWith("-", StringComparison.Ordinal))
                {
                    if (IsValueOption(a) && i + 1 < args.Length)
                    {
                        cli._options[Normalize(a)] = args[++i];
                    }
                    else
                    {
                        cli._flags.Add(Normalize(a));
                    }
                    continue;
                }

                positional.Add(a);
            }

            if (positional.Count > 0) cli.Verb = positional[0];
            if (positional.Count > 1) cli.SubVerb = positional[1];

            return cli;
        }

        public string Option(string name, string fallback)
        {
            string value;
            if (_options.TryGetValue(Normalize(name), out value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            return fallback;
        }

        public bool Flag(string name)
        {
            return _flags.Contains(Normalize(name));
        }

        public bool Bool(string name, bool fallback)
        {
            string raw = Option(name, null);
            if (string.IsNullOrWhiteSpace(raw)) return _flags.Contains(Normalize(name)) ? true : fallback;

            string v = raw.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }

        private static bool IsValueOption(string arg)
        {
            string normalized = Normalize(arg);
            for (int i = 0; i < ValueOptions.Length; i++)
            {
                if (string.Equals(ValueOptions[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Normalize(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "";
            return arg.StartsWith("--", StringComparison.Ordinal) ? arg : "--" + arg.TrimStart('-');
        }
    }
}
