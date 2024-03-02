using System;
using System.Collections.Generic;
using System.IO;

namespace System
{
    public class INI
    {
        static bool IsLinebreak(char chr) => chr == '\r' || chr == '\n';
        public string path;
        public INI(string INIPath)
        {
            path = Path.GetFullPath(INIPath);
        }
        public Dictionary<string, Dictionary<string, string>> ReadAllValues()
        {
            var data = new Dictionary<string, Dictionary<string, string>>();
            if (File.Exists(path))
            {
                var section = new Dictionary<string, string>();
                HandleINI((r, sectionName, key, value, s) =>
                {
                    if (!data.TryGetValue(sectionName, out section))
                        section = data[sectionName] = new Dictionary<string, string>();
                    section[key] = value;
                    return true;
                });
            }
            return data;
        }
        static bool Equal(string a, string b) => a.ToLowerInvariant().Equals(b.ToLowerInvariant());

        public string ReadValue(string section, string key)
        {
            string result = null;
            if (File.Exists(path))
            {
                HandleINI((r, sectionName, fKey, value, s) =>
                {
                    if (!Equals(sectionName,section) || !Equals(fKey,key))
                        return true;
                    result = value;
                    return false;
                });
            }
            return result;
        }
        public string[] ReadValues(string section, params string[] keys)
        {
            string[] results = new string[keys.Length];
            var c = 0;
            if (File.Exists(path))
            {
                HandleINI((r, sectionName, fKey, value, s) =>
                {
                    if (Equals(sectionName, section))
                        for (int i = 0; i < keys.Length; i++)
                            if (results[i] == null && Equals(fKey, keys[i]))
                            {
                                results[i] = value;
                                c++;
                                if (c >= results.Length)
                                    return false;
                            }
                    return true;
                });
            }
            return results;
        }

        /// <summary>
        /// Replaces a value in the file. If <paramref name="value"/> is <see langword="null"/>, the key will be removed
        /// </summary>
        /// <returns><see langword="true"/> If the value is successfully set. Otherwise <see langword="false"/>; this can be caused by the file, section or key being missing.</returns>
        public bool SetValue(string section, string key, string value)
        {
            if (!File.Exists(path))
                return false;
            var flag = false;
            bool Handle(StringStream stream, string fSection, string fKey, string fValue, int lineStart)
            {
                if (Equal(fSection, section) && Equal(fKey, key))
                {
                    if (value == null)
                    {

                        var end = stream.Position;
                        stream.Position = lineStart;
                        while (stream.Peek() != null && (stream.Position <= end || IsLinebreak(stream.Peek().Value)))
                            stream.Remove();
                    }
                    else
                    {
                        stream.Position -= fValue.Length;
                        var l = Math.Max(fValue.Length, value.Length);
                        for (int i = 0; i < l; i++)
                            if (i >= fValue.Length)
                                stream.Write(value[i]);
                            else if (i >= value.Length)
                                stream.Remove();
                            else
                                stream.Rewrite(value[i]);
                    }
                    flag = true;
                    return false;
                }
                return true;
            }
            HandleINI(Handle);
            return flag;
        }

        /// <summary>
        /// <para>Adds a value to the file. If the file does not exist, it is created.<br/>If the section does not exist in the file, it is created.</para>
        /// <para>Note: this does NOT check if the value already exists. </para>
        /// </summary>
        /// <param name="addToStart">If <see langword="true"/>, the <paramref name="key"/>/<paramref name="value"/> will be added to the start of the <paramref name="section"/>, otherwise it will be placed at the end</param>
        public void AddValue(string section, string key, string value, bool addToStart = false)
        {
            if (!File.Exists(path))
            {
                File.WriteAllLines(path, new[] { "[" + section + "]", key + "=" + value });
                return;
            }
            bool flag = true;
            bool Handle(StringStream stream, string fSection)
            {
                if (!Equals(fSection, section))
                    return true;
                stream.Write(addToStart ? ("\r\n" + key + "=" + value) : (key + "=" + value + "\r\n"));
                flag = false;
                return false;
            }
            if (addToStart)
                HandleINI(onSectionStart: Handle);
            else
                HandleINI(onSectionEnd: Handle);
            if (flag)
                File.AppendAllText(path, Environment.NewLine + "[" + section + "]" + Environment.NewLine + key + "=" + value);
        }

        /// <summary>
        /// <para>Replaces a value in the file. If <paramref name="value"/> is <see langword="null"/>, the key will be removed.<br/>
        /// If the value is not present in the file, it adds a value to the file.<br/>
        /// If the file does not exist, it is created. If the section does not exist in the file, it is created.</para>
        /// 
        /// <para>This is a combination of <see cref="SetValue"/> and <see cref="AddValue"/></para>
        /// </summary>
        public void WriteValue(string section, string key, string value)
        {
            if (!SetValue(section, key, value) && value != null)
                AddValue(section, key, value);
        }

        delegate bool ValueFound(StringStream stream, string Section, string Key, string Value, int LineStart);
        delegate bool SectionEvent(StringStream stream, string Section);

        void HandleINI(ValueFound onValueFound = null, SectionEvent onSectionStart = null, SectionEvent onSectionEnd = null)
        {
            if (onValueFound == null)
                onValueFound = delegate { return true; };
            if (onSectionStart == null)
                onSectionStart = delegate { return true; };
            if (onSectionEnd == null)
                onSectionEnd = delegate { return true; };
            string readingSection = null;
            var sectionComplete = false;
            string readingKey = null;
            string readingValue = null;
            bool lineClosed = false;
            bool sectionOpened = false;
            int lineStart = 0;
            bool lineEnded = true;
            var stream = new StringStream(File.ReadAllText(path));
            while (true)
            {
                var raw = stream.Peek();
                var closing = raw == null;
                var chr = raw ?? '\n';
                if (IsLinebreak(chr))
                {
                    if (readingSection != null && !sectionComplete)
                        readingSection = null;
                    if (readingKey != null && readingValue != null && readingSection != null && !onValueFound(stream, readingSection, readingKey, readingValue, lineStart))
                        break;
                    if (sectionOpened && !onSectionStart(stream, readingSection))
                        break;
                    sectionOpened = false;
                    readingValue = null;
                    readingKey = null;
                    lineClosed = false;
                    lineEnded = true;
                    if (closing)
                        onSectionEnd(stream, readingSection);

                }
                else if (!lineClosed)
                {
                    if (lineEnded)
                    {
                        lineEnded = false;
                        lineStart = stream.Position;
                    }
                    if ((chr == ';' || chr == '#') && readingKey == null && sectionComplete)
                        lineClosed = true;
                    else if (readingKey == null && chr == '[')
                    {
                        if (sectionComplete)
                        {
                            var p = stream.Position;
                            stream.Position = lineStart;
                            if (!onSectionEnd(stream, readingSection))
                                break;
                            var newPos = stream.Position - lineStart + p;
                            lineStart = stream.Position;
                            stream.Position = newPos;
                        }
                        readingSection = "";
                        sectionComplete = false;
                    }
                    else if (readingSection != null && !sectionComplete)
                    {
                        if (chr == ']')
                        {
                            sectionOpened = true;
                            sectionComplete = true;
                            lineClosed = true;
                        }
                        else
                            readingSection += chr;
                    }
                    else if (readingSection != null)
                    {
                        if (readingKey == null)
                        {
                            if (!char.IsWhiteSpace(chr))
                                readingKey = "";
                        }
                        if (readingKey != null)
                        {
                            if (readingValue != null)
                                readingValue += chr;
                            else if (chr == '=')
                                readingValue = "";
                            else
                                readingKey += chr;
                        }
                    }
                }
                if (stream.Peek() == null)
                    break;
                stream.Position++;
            }
            if (stream.Edited)
                File.WriteAllText(path, stream.ToString());
        }
    }

    public class StringStream
    {
        List<char> data;
        public int Position;
        public int Length => data.Count;
        public bool Edited = false;
        public StringStream(string str) => data = new List<char>(str ?? "");
        public override string ToString()
        {
            var r = "";
            foreach (var c in data)
                r += c;
            return r;
        }

        public char? Read() => Position >= data.Count ? default(char?) : data[Position++];
        public char? Peek() => Position >= data.Count ? default(char?) : data[Position];
        public char this[int position]
        {
            get => data[position];
            set
            {
                Edited = true;
                data[position] = value;
            }
        }
        public void Write(string value)
        {
            Edited = true;
            if (Position >= Length)
            {
                data.AddRange(value);
                Position = Length;
                return;
            }
            data.InsertRange(Position, value);
            Position += value.Length;
        }
        public void Write(char value)
        {
            Edited = true;
            if (Position >= Length)
            {
                data.Add(value);
                Position = Length;
                return;
            }
            data.Insert(Position++, value);
        }
        public void Rewrite(char value)
        {
            Edited = true;
            if (Position >= Length)
            {
                data.Add(value);
                Position = Length;
                return;
            }
            data[Position++] = value;
        }
        public void Remove()
        {
            if (Position >= Length)
                return;
            Edited = true;
            data.RemoveAt(Position);
        }
    }
}