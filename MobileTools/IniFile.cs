using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace System
{
    public class IniFile
    {
        public readonly List<IniLine> Header = new List<IniLine>();
        public readonly List<IniSection> Sections = new List<IniSection>();
        public static IniFile FromFile(string path, bool throwOnError = true)
        {
            using (var s = new StreamReader(path, Encoding.UTF8, true))
                return FromStream(s, throwOnError);
        }
        public static IniFile FromString(string data, bool throwOnError = true)
        {
            using (var s = new StringReader(data))
                return FromStream(s, throwOnError);
        }
        public static IniFile FromStream(TextReader data, bool throwOnError = true)
        {
            var f = new IniFile();
            if (data == null)
                return f;
            int state = -1; // -1 nothing, 0 reading key, 1 reading value, 2 reading section name, 3 read section name, 4 reading comment
            char last;
            char tmp = '\0';
            bool escape = false;
            var lines = f.Header;
            var lineCount = 0;
            var current = new StringBuilder();
            while (true)
            {
                if (data == null)
                    break;
                var _c = data.Read();
                char c;
                if (_c != -1)
                    c = (char)_c;
                else
                {
                    data = null;
                    escape = false;
                    c = '\n';
                }
                if (escape)
                {
                    escape = false;
                    current.Append(c);
                    if (state == -1)
                    {
                        state = 0;
                        lines.Add(new IniLine());
                    }
                }
                else if (c == '\\')
                    escape = true;
                else
                {
                    last = tmp;
                    tmp = c;
                    if (c == '\n' || c == '\r')
                    {
                        if (c == '\n' && last == '\r')
                            continue;
                        lineCount++;
                        if (state == -1)
                            continue;
                        if (state == 0)
                        {
                            if (throwOnError)
                                throw new FormatException($"INI parse error on line {lineCount}. Key without value");
                            lines.RemoveAt(lines.Count - 1);
                        }
                        if (state == 2)
                        {
                            if (throwOnError)
                                throw new FormatException($"INI parse error on line {lineCount}. Section name not closed");
                            f.Sections[f.Sections.Count - 1].Name = current.ToString();
                        }
                        if (state == 1)
                            lines[lines.Count - 1].Value = current.ToString();
                        if (state == 4)
                        {
                            if (lines.Count > 0)
                                lines[lines.Count - 1].Comment = current.ToString();
                            else
                                f.Sections[f.Sections.Count - 1].Comment = current.ToString();
                        }
                        state = -1;
                        current.Clear();
                        continue;
                    }
                    if (state == 4)
                    {
                        current.Append(c);
                        continue;
                    }
                    if (state == 2)
                    {
                        if (c == ']')
                        {
                            state = 3;
                            f.Sections[f.Sections.Count - 1].Name = current.ToString();
                            current.Clear();
                            continue;
                        }
                        current.Append(c);
                        continue;
                    }
                    if (c == '#' || c == ';')
                    {
                        if (state == -1)
                            lines.Add(new IniLine());
                        if (state == 0 && throwOnError)
                            throw new FormatException($"INI parse error on line {lineCount + 1}. Key without value");
                        if (state == 1)
                            lines[lines.Count - 1].Value = current.ToString();
                        if (state == 2)
                        {
                            if (throwOnError)
                                throw new FormatException($"INI parse error on line {lineCount + 1}. Section name not closed");
                            f.Sections[f.Sections.Count -1 ].Name = current.ToString();
                        }
                        state = 4;
                        current.Clear();
                        continue;
                    }
                    if (state == -1)
                    {
                        if (char.IsWhiteSpace(c))
                            continue;
                        if (c == '[')
                        {
                            state = 2;
                            f.Sections.Add(new IniSection());
                            lines = f.Sections[f.Sections.Count - 1].Lines;
                            continue;
                        }
                        if (c == '=')
                        {
                            lines.Add(new IniLine() { Key = "" });
                            state = 1;
                            continue;
                        }
                        state = 0;
                        lines.Add(new IniLine());
                        current.Append(c);
                        continue;
                    }
                    if (state == 3)
                    {
                        if (char.IsWhiteSpace(c))
                            continue;
                        if (throwOnError)
                            throw new FormatException($"INI parse error on line {lineCount + 1}. Non-comment text on the same line as section name");
                        continue;
                    }
                    if (state == 1)
                    {
                        current.Append(c);
                        continue;
                    }
                    if (state == 0)
                    {
                        if (c == '=')
                        {
                            lines[lines.Count - 1].Key = current.ToString();
                            current.Clear();
                            state = 1;
                            continue;
                        }
                        current.Append(c);
                        continue;
                    }
                    throw new Exception($"This should never happen [state={state},current={current},line={lineCount + 1}]");
                }
            }
            return f;
        }
        public void ToFile(string path)
        {
            using (var s = new StreamWriter(path, false, Encoding.UTF8))
                ToStream(s);
        }
        public override string ToString() => AppendTo(new StringBuilder()).ToString();
        public StringBuilder AppendTo(StringBuilder builder)
        {
            using (var s = new StringBuilderWriter(builder))
                ToStream(s);
            return builder;
        }
        public void ToStream(TextWriter stream)
        {
            for (int i = -1; i < Sections.Count; i++)
            {
                var s = i == -1 ? null : Sections[i];
                if (s != null)
                {
                    stream.Write('[');
                    foreach (var c in s.Name)
                    {
                        if (c == ']' || c == '\\' || c == '\r' || c == '\n')
                            stream.Write('\\');
                        stream.Write(c);
                    }
                    stream.Write(']');
                    if (s.Comment != null)
                    {
                        stream.Write(" #");
                        foreach (var c in s.Comment)
                        {
                            if (c == '\\' || c == '\r' || c == '\n')
                                stream.Write('\\');
                            stream.Write(c);
                        }
                    }
                    stream.WriteLine();
                }
                var lines = s?.Lines ?? Header;
                foreach (var l in lines)
                {
                    if (l.Key != null || l.Value != null)
                    {
                        if (l.Key?.Length > 0 && (l.Key[0] == '[' || char.IsWhiteSpace(l.Key[0])))
                            stream.Write('\\');
                        if (l.Key != null)
                            foreach (var c in l.Key)
                            {
                                if (c == '=' || c == '\\' || c == '\r' || c == '\n' || c == '#' || c == ';')
                                    stream.Write('\\');
                                stream.Write(c);
                            }
                        stream.Write('=');
                        if (l.Value != null)
                            foreach (var c in l.Value)
                            {
                                if (c == '\\' || c == '\r' || c == '\n' || c == '#' || c == ';')
                                    stream.Write('\\');
                                stream.Write(c);
                            }
                        if (l.Comment != null)
                            stream.Write('#');
                    }
                    else if (l.Comment != null)
                        stream.Write(" #");

                    if (l.Comment != null)
                        foreach (var c in l.Comment)
                        {
                            if (c == '\\' || c == '\r' || c == '\n')
                                stream.Write('\\');
                            stream.Write(c);
                        }
                    stream.WriteLine();
                }
            }
        }

        class StringBuilderWriter : TextWriter
        {
            public StringBuilder builder;
            public StringBuilderWriter(StringBuilder builder) { this.builder = builder; }

            public override Encoding Encoding => Encoding.Unicode;
            public override void Write(char value) => builder.Append(value);
            public override void Write(string value) => builder.Append(value);
            public override void Write(char[] buffer,int index, int count) => builder.Append(buffer,index,count);
        }
    }
    public class IniSection
    {

        public string Name;
        public string Comment;
        public readonly List<IniLine> Lines = new List<IniLine>();
    }
    public class IniLine
    {
        public string Key;
        public string Value;
        public string Comment;
    }
}
