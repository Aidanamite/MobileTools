using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UnhollowerBaseLib;
using UnityEngine;
using System.IO;
using Object = UnityEngine.Object;
using IniFile = System.IniFile;
using HarmonyLib;
using System.Threading;
using System.Globalization;
using System.Collections.Concurrent;

[assembly: MelonInfo(typeof(MobileTools.Main), "Mobile Tools", MobileTools.Main.VERSION, "Aidanamite")]

namespace MobileTools
{
    public class Main : MelonMod
    {
        public const string VERSION = "1.1.0";
        public static string ConfigFolder = "/storage/emulated/0/MelonLoader/com.KnowledgeAdventure.SchoolOfDragons/Config";
        internal static Dictionary<MelonMod, ConfigFile> configFiles = new();
        internal static Dictionary<string, MelonMod> configFileToMod = new();
        static MelonLogger.Instance logger;

        public override void OnEarlyInitializeMelon()
        {
            ConfigFolder = new DirectoryInfo(typeof(Main).Assembly.Location).Parent.Parent.CreateSubdirectory("Config").FullName;
            LoggerInstance.Msg("Config path: " + ConfigFolder);
            // Disabled code until I find a way to request storage write permisions. So far all the UnityEngine.Android.Permission functions throw "NotSupportedException: Method unstripping failed"
            /*var pendingRequest = true;
            //if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.MANAGE_EXTERNAL_STORAGE"))
                UnityEngine.Android.Permission.RequestUserPermission("android.permission.MANAGE_EXTERNAL_STORAGE", new UnityEngine.Android.PermissionCallbacks()
                {
                    PermissionDenied = new ActionHolder(() =>
                    {
                        ConfigFolder = "/storage/emulated/0/Android/data/com.KnowledgeAdventure.SchoolOfDragons/files/Config";
                        pendingRequest = false;
                    }).GetAction(),
                    PermissionDeniedAndDontAskAgain = new ActionHolder(() =>
                    {
                        ConfigFolder = "/storage/emulated/0/Android/data/com.KnowledgeAdventure.SchoolOfDragons/files/Config";
                        pendingRequest = false;
                    }).GetAction(),
                    PermissionGranted = new ActionHolder(() => pendingRequest = false).GetAction()
                });
            while (pendingRequest)
                Thread.Sleep(10);*/
            mainThread = Thread.CurrentThread;
            logger = LoggerInstance;
            base.OnEarlyInitializeMelon();
            if (!Directory.Exists(ConfigFolder))
                try
                {
                    Directory.CreateDirectory(ConfigFolder);
                }
                catch
                {
                    ConfigFolder = "/storage/emulated/0/Android/data/com.KnowledgeAdventure.SchoolOfDragons/files/Config";
                    if (!Directory.Exists(ConfigFolder))
                            Directory.CreateDirectory(ConfigFolder);
                }
            void SetupConfig(MelonMod mod)
            {
                
                if (mod != null && mod != this)
                {
                    List<(FieldInfo, ConfigFieldAttribute)> l = new();
                    foreach (var f in mod.GetType().GetFields(~BindingFlags.Default))
                    {
                        var a = f.GetCustomAttribute<ConfigFieldAttribute>();
                        if (a != null)
                            l.Add((f, a));
                    }
                    if (l.Count != 0)
                    {
                        foreach (var m in mod.GetType().GetMethods(~BindingFlags.Default))
                            if (m.GetCustomAttribute<OnBeforeConfigLoadAttribute>() != null)
                            {
                                if (!m.ContainsGenericParameters && m.GetParameters().Length == 0)
                                {
                                    try
                                    {
                                        m.Invoke(mod, []);
                                    }
                                    catch (Exception e)
                                    {
                                        LogError(e);
                                    }
                                }
                                LogWarning("Method " + m + " is not suitable for OnBeforeConfigLoad. OnBeforeConfigLoad methods cannot have parameters");
                            }
                        var file = ConfigFolder + "/" + mod.Info.Author + "." + mod.Info.Name + ".ini";
                        configFiles[mod] = new(file, mod, l);
                        configFileToMod[file.ToLowerInvariant()] = mod;
                    }
                }
            }
            foreach (var m in MelonBase.RegisteredMelons)
                if (m is MelonMod m2)
                    SetupConfig(m2);
            OnMelonRegistered.Subscribe((x) => SetupConfig(x as MelonMod));
            var watcher = new FileSystemWatcher(ConfigFolder, "*.ini");
            watcher.Changed += OnConfigFileChanged;
            watcher.Renamed += OnConfigFileChanged;
            watcher.Created += OnConfigFileChanged;
            watcher.EnableRaisingEvents = true;
            HarmonyInstance.PatchAll();
            LogInfo("Initialized");
        }

        internal static bool writing = false;
        public void OnConfigFileChanged(object sender, FileSystemEventArgs args)
        {
            if (writing)
                return;
            if (mainThread != Thread.CurrentThread)
            {
                pending.Enqueue(() => OnConfigFileChanged(sender, args));
                return;
            }
            if (configFileToMod.TryGetValue(args.FullPath.ToLowerInvariant(), out var mod))
                configFiles[mod].Load();
        }
        static Thread mainThread;
        static ConcurrentQueue<Action> pending = new();
        public override void OnUpdate()
        {
            base.OnUpdate();
            while (pending.TryDequeue(out var a))
                try
                {
                    a.Invoke();
                } catch (Exception e)
                {
                    LogError(e);
                }
        }

        static bool firstLoad = true;
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            base.OnSceneWasLoaded(buildIndex, sceneName);
            if (firstLoad)
            {
                firstLoad = false;
                var g = new GameObject("PauseWatcher");
                Object.DontDestroyOnLoad(g);
                g.AddComponent<PauseWatcher>();
            }
        }

        static Dictionary<string, DateTime> changeMemory = new();
        static bool changeMemPopulated = false;
        public static void OnFocusChanged(bool isFocused)
        {
            if (isFocused)
            {
                if (changeMemPopulated)
                    foreach (var f in configFileToMod)
                        if (File.Exists(f.Key) && (!changeMemory.TryGetValue(f.Key, out var time) || time != File.GetLastWriteTimeUtc(f.Key)))
                            ConfigFile.Reload(f.Value);
                changeMemPopulated = false;
            }
            else
            {
                changeMemPopulated = true;
                changeMemory.Clear();
                foreach (var f in configFileToMod.Keys)
                    if (File.Exists(f))
                        changeMemory[f] = File.GetLastWriteTimeUtc(f);
            }
        }

        public static void LogInfo(object msg) => logger.Msg(msg);
        public static void LogError(object msg) => logger.Error(msg);
        public static void LogWarning(object msg) => logger.Warning(msg);
    }

    [RegisterTypeInIl2Cpp]
    public class PauseWatcher : MonoBehaviour
    {
        public PauseWatcher(IntPtr intPtr) : base(intPtr) { }
        public void OnApplicationFocus(bool focus) => Main.OnFocusChanged(focus);
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ConfigFieldAttribute : Attribute
    {
        public string Section;
        public string KeyOverride;
        public ConfigFieldAttribute(string section = "Config", string keyOverride = null)
        {
            Section = section;
            KeyOverride = keyOverride;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class OnConfigLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public class OnBeforeConfigLoadAttribute : Attribute { }

    internal interface ConfigTypeParser
    {
        internal object ToObject(string value);
        internal string ToString(object obj);
    }
    public abstract class ConfigTypeParser<T> : ConfigTypeParser
    {
        string ConfigTypeParser.ToString(object obj) => ToString((T)obj);
        object ConfigTypeParser.ToObject(string value) => ToObject(value);
        public abstract T ToObject(string value);
        public abstract string ToString(T obj);
        public static void Register(ConfigTypeParser<T> parser) => ConfigFile.parsers[typeof(T)] = parser;
        public void Register() => Register(this);
    }

    public class ConfigFile
    {
        string file;
        internal Dictionary<string, Dictionary<string, FieldInfo>> configs = new();
        internal static Dictionary<Type, ConfigTypeParser> parsers = new();
        static ConfigFile()
        {
            new DefaultParsers.BasicParser<bool>().Register();
            new DefaultParsers.BasicParser<sbyte>().Register();
            new DefaultParsers.BasicParser<byte>().Register();
            new DefaultParsers.BasicParser<short>().Register();
            new DefaultParsers.BasicParser<ushort>().Register();
            new DefaultParsers.BasicParser<int>().Register();
            new DefaultParsers.BasicParser<uint>().Register();
            new DefaultParsers.BasicParser<long>().Register();
            new DefaultParsers.BasicParser<ulong>().Register();
            new DefaultParsers.BasicParser<string>().Register();
            new DefaultParsers.BasicParser<float>().Register();
            new DefaultParsers.BasicParser<double>().Register();
            new DefaultParsers.DateTimeParser().Register();
            new DefaultParsers.TimeSpanParser().Register();
        }
        List<MethodInfo> onLoad = new();
        object target;
        public ConfigFile(string path, object target, IEnumerable<(FieldInfo,ConfigFieldAttribute)> fields)
        {
            this.target = target;
            foreach (var m in target.GetType().GetMethods(~BindingFlags.Default))
                if (m.GetCustomAttribute<OnConfigLoadAttribute>() != null)
                {
                    if (!m.ContainsGenericParameters && m.GetParameters().Length == 0)
                    {
                        onLoad.Add(m);
                        break;
                    }
                    Main.LogWarning("Method " + m + " is not suitable for OnConfigLoad. OnConfigLoad methods cannot have parameters");
                }
            if (!File.Exists(path))
                File.WriteAllBytes(path, []);
            file = path;
            foreach (var p in fields)
                configs.GetOrCreate(p.Item2.Section)[p.Item2.KeyOverride ?? p.Item1.Name] = p.Item1;
            try
            {
                Load();
            } catch (Exception e)
            {
                Main.LogError(e);
            }
        }
        public void Load()
        {
            var missing = false;
            try
            {
                var values = IniFile.FromFile(file);
                foreach (var s in configs)
                {
                    var section = values.Sections.Find(x => x.Name == s.Key);
                    if (section == null)
                    {
                        Main.LogInfo($"Section not found for \"{s.Key}\"");
                        missing = true;
                    }
                    else
                        foreach (var c in s.Value)
                        {
                            var value = section.Lines.Find(x => x.Key == c.Key);
                            if (value == null)
                            {
                                Main.LogInfo($"Entry not found for \"{c.Key}\" ({s.Key})");
                                missing = true;
                            }
                            else
                            {
                                var t = c.Value.FieldType;
                                object parsed;
                                var nullable = t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
                                if (nullable)
                                    t = t.GetGenericArguments()[0];
                                if (nullable && value.Value == "")
                                    parsed = null;
                                else
                                {
                                    try
                                    {
                                        if (parsers.TryGetValue(t, out var parser))
                                            parsed = parser.ToObject(value.Value);
                                        else if (t.IsEnum)
                                            parsed = DefaultParsers.EnumParser.ToObject(t, value.Value);
                                        else
                                        {
                                            Main.LogError("Class " + c.Value.FieldType.FullName + " is not a supported configuration type. Use ConfigTypeParser<" + c.Value.FieldType.FullName + ">.Register() to add support for another type");
                                            continue;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Main.LogError(e);
                                        continue;
                                    }
                                    if (nullable)
                                        parsed = c.Value.FieldType.GetConstructor([t]).Invoke([parsed]);
                                }
                                try
                                {
                                    c.Value.SetValue(target, parsed);
                                }
                                catch (Exception e)
                                {
                                    Main.LogError(e);
                                }
                            }
                        }
                }
            }
            catch (Exception e)
            {
                Main.LogWarning(e);
                missing = true;
            }
            if (missing)
                Save();
            foreach (var m in onLoad)
                try
                {
                    m?.Invoke(target, []);
                }
                catch (Exception e)
                {
                    Main.LogError(e);
                }
        }
        public void Save()
        {
            if (!Directory.Exists(Main.ConfigFolder))
                Directory.CreateDirectory(Main.ConfigFolder);
            Main.writing = true;
            var f = new IniFile();
            foreach (var s in configs)
            {
                var section = new IniSection() { Name = s.Key };
                f.Sections.Add(section);
                foreach (var c in s.Value)
                {
                    var t = c.Value.FieldType;
                    var value = c.Value.GetValue(target);
                    string parsed;
                    var nullable = t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
                    if (nullable)
                        t = t.GetGenericArguments()[0];
                    try
                    {
                        if (nullable && value == null)
                            parsed = "";
                        else if (parsers.TryGetValue(t, out var parser))
                            parsed = parser.ToString(value);
                        else if (t.IsEnum)
                            parsed = DefaultParsers.EnumParser.ToString(value);
                        else
                            continue;
                    }
                    catch (Exception e)
                    {
                        Main.LogError(e);
                        continue;
                    }
                    try
                    {
                        section.Lines.Add(new IniLine() { Key = c.Key, Value = parsed });
                    }
                    catch (Exception e)
                    {
                        Main.LogError(e);
                    }
                }
            }
            f.ToFile(file);
            Main.writing = false;
        }
        public static void Reload(MelonMod mod)
        {
            if (Main.configFiles.TryGetValue(mod, out var file))
                file.Load();
        }
        public static void Save(MelonMod mod)
        {
            if (Main.configFiles.TryGetValue(mod, out var file))
                file.Save();
        }
    }

    public static class ExtentionMethods
    {
        public static Y GetOrCreate<X,Y>(this IDictionary<X,Y> d, X key) where Y : new()
        {
            if (!d.TryGetValue(key, out var v))
                d[key] = v = new();
            return v;
        }
        public static Il2CppSystem.Type ToIl2Cpp(this Type type) => Il2CppSystem.Type.internal_from_handle(type.TypeHandle.Value);
        public static bool Is<T>(this Il2CppSystem.Object obj) where T : Il2CppSystem.Object => obj.Is<T>(out _);
        public static bool Is<T>(this Il2CppSystem.Object obj,out T nObj) where T : Il2CppSystem.Object
        {
            nObj = obj.TryCast<T>();
            return nObj != null;
        }
        //public static Y GetOrCreateValue<X, Y>(this Il2CppSystem.Runtime.CompilerServices.ConditionalWeakTable<X, Y> table, X key) where X : Il2CppSystem.Object where Y : Il2CppSystem.Object => table.GetValue(key, WeakTableCreateConverter<X,Y>.ConvertAction((x) => typeof(Y).ToIl2Cpp(). ));
    }

    public static class DefaultParsers
    {
        internal class BasicParser<T> : ConfigTypeParser<T> where T : IConvertible
        {
            public override T ToObject(string value) => (T)(value as IConvertible).ToType(typeof(T),CultureInfo.InvariantCulture);
            public override string ToString(T obj) => obj?.ToString(CultureInfo.InvariantCulture) ?? "";
        }
        internal class DateTimeParser : ConfigTypeParser<DateTime>
        {
            public override DateTime ToObject(string value) => DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture);
            public override string ToString(DateTime obj) => obj.ToString("O", CultureInfo.InvariantCulture);
        }
        internal class TimeSpanParser : ConfigTypeParser<TimeSpan>
        {
            public override TimeSpan ToObject(string value) => new TimeSpan(long.Parse(value, CultureInfo.InvariantCulture));
            public override string ToString(TimeSpan obj) => obj.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        internal static class EnumParser
        {
            public static object ToObject(Type enumType, string value) => Enum.Parse(enumType, value, true);
            public static string ToString(object obj) => obj.ToString();
        }
    }

    public class DelegateConverter<A, B, C> : Il2CppSystem.Object where A : Delegate where B : Il2CppSystem.Delegate where C : DelegateConverter<A,B,C>, new()
    {
        protected A Delegate;
        public static B ConvertAction(A Delegate) => Delegate == null ? null : (B)Il2CppSystem.Delegate.CreateDelegate(typeof(B).ToIl2Cpp(), new C() { Delegate = Delegate }, typeof(C).ToIl2Cpp().GetMethod("Invoke", ~Il2CppSystem.Reflection.BindingFlags.Default));
    }
    public class ActionConverter : DelegateConverter<Action,Il2CppSystem.Action, ActionConverter>
    {
        void Invoke() => Delegate.Invoke();
    }
    public class ActionConverter<A> : DelegateConverter<Action<A>, Il2CppSystem.Action<A>, ActionConverter<A>>
    {
        void Invoke(A a) => Delegate.Invoke(a);
    }
    public class ActionConverter<A, B> : DelegateConverter<Action<A, B>, Il2CppSystem.Action<A, B>, ActionConverter<A, B>>
    {
        void Invoke(A a, B b) => Delegate.Invoke(a, b);
    }
    public class ActionConverter<A, B, C> : DelegateConverter<Action<A, B, C>, Il2CppSystem.Action<A, B, C>, ActionConverter<A, B, C>>
    {
        void Invoke(A a, B b, C c) => Delegate.Invoke(a, b, c);
    }
    public class ActionConverter<A, B, C, D> : DelegateConverter<Action<A, B, C, D>, Il2CppSystem.Action<A, B, C, D>, ActionConverter<A, B, C, D>>
    {
        void Invoke(A a, B b, C c, D d) => Delegate.Invoke(a, b, c, d);
    }
    public class ActionConverter<A, B, C, D, E> : DelegateConverter<Action<A, B, C, D, E>, Il2CppSystem.Action<A, B, C, D, E>, ActionConverter<A, B, C, D, E>>
    {
        void Invoke(A a, B b, C c, D d, E e) => Delegate.Invoke(a, b, c, d, e);
    }
    public class ActionConverter<A, B, C, D, E, F> : DelegateConverter<Action<A, B, C, D, E, F>, Il2CppSystem.Action<A, B, C, D, E, F>, ActionConverter<A, B, C, D, E, F>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f) => Delegate.Invoke(a, b, c, d, e, f);
    }
    public class ActionConverter<A, B, C, D, E, F, G> : DelegateConverter<Action<A, B, C, D, E, F, G>, Il2CppSystem.Action<A, B, C, D, E, F, G>, ActionConverter<A, B, C, D, E, F, G>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g) => Delegate.Invoke(a, b, c, d, e, f, g);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H> : DelegateConverter<Action<A, B, C, D, E, F, G, H>, Il2CppSystem.Action<A, B, C, D, E, F, G, H>, ActionConverter<A, B, C, D, E, F, G, H>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h) => Delegate.Invoke(a, b, c, d, e, f, g, h);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I>, ActionConverter<A, B, C, D, E, F, G, H, I>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i) => Delegate.Invoke(a, b, c, d, e, f, g, h, i);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J>, ActionConverter<A, B, C, D, E, F, G, H, I, J>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K, L>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K, L>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K, L, M>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K, L, M>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n, O o) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n, o);
    }
    public class ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P> : DelegateConverter<Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>, Il2CppSystem.Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>, ActionConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>>
    {
        void Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n, O o, P p) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p);
    }
    public class FuncConverter<A> : DelegateConverter<Func<A>, Il2CppSystem.Func<A>, FuncConverter<A>>
    {
        A Invoke() => Delegate.Invoke();
    }
    public class FuncConverter<A, B> : DelegateConverter<Func<A, B>, Il2CppSystem.Func<A, B>, FuncConverter<A, B>>
    {
        B Invoke(A a) => Delegate.Invoke(a);
    }
    public class FuncConverter<A, B, C> : DelegateConverter<Func<A, B, C>, Il2CppSystem.Func<A, B, C>, FuncConverter<A, B, C>>
    {
        C Invoke(A a, B b) => Delegate.Invoke(a, b);
    }
    public class FuncConverter<A, B, C, D> : DelegateConverter<Func<A, B, C, D>, Il2CppSystem.Func<A, B, C, D>, FuncConverter<A, B, C, D>>
    {
        D Invoke(A a, B b, C c) => Delegate.Invoke(a, b, c);
    }
    public class FuncConverter<A, B, C, D, E> : DelegateConverter<Func<A, B, C, D, E>, Il2CppSystem.Func<A, B, C, D, E>, FuncConverter<A, B, C, D, E>>
    {
        E Invoke(A a, B b, C c, D d) => Delegate.Invoke(a, b, c, d);
    }
    public class FuncConverter<A, B, C, D, E, F> : DelegateConverter<Func<A, B, C, D, E, F>, Il2CppSystem.Func<A, B, C, D, E, F>, FuncConverter<A, B, C, D, E, F>>
    {
        F Invoke(A a, B b, C c, D d, E e) => Delegate.Invoke(a, b, c, d, e);
    }
    public class FuncConverter<A, B, C, D, E, F, G> : DelegateConverter<Func<A, B, C, D, E, F, G>, Il2CppSystem.Func<A, B, C, D, E, F, G>, FuncConverter<A, B, C, D, E, F, G>>
    {
        G Invoke(A a, B b, C c, D d, E e, F f) => Delegate.Invoke(a, b, c, d, e, f);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H> : DelegateConverter<Func<A, B, C, D, E, F, G, H>, Il2CppSystem.Func<A, B, C, D, E, F, G, H>, FuncConverter<A, B, C, D, E, F, G, H>>
    {
        H Invoke(A a, B b, C c, D d, E e, F f, G g) => Delegate.Invoke(a, b, c, d, e, f, g);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I>, FuncConverter<A, B, C, D, E, F, G, H, I>>
    {
        I Invoke(A a, B b, C c, D d, E e, F f, G g, H h) => Delegate.Invoke(a, b, c, d, e, f, g, h);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J>, FuncConverter<A, B, C, D, E, F, G, H, I, J>>
    {
        J Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i) => Delegate.Invoke(a, b, c, d, e, f, g, h, i);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K>>
    {
        K Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L>>
    {
        L Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L, M>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L, M>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M>>
    {
        M Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N>>
    {
        N Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>>
    {
        O Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>>
    {
        P Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n, O o) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n, o);
    }
    public class FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q> : DelegateConverter<Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q>, Il2CppSystem.Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q>, FuncConverter<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q>>
    {
        Q Invoke(A a, B b, C c, D d, E e, F f, G g, H h, I i, J j, K k, L l, M m, N n, O o, P p) => Delegate.Invoke(a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p);
    }
    /*public class WeakTableCreateConverter<X,Y> : DelegateConverter<Func<X,Y>, Il2CppSystem.Runtime.CompilerServices.ConditionalWeakTable<X,Y>.CreateValueCallback, WeakTableCreateConverter<X, Y>> where X : class where Y : class
    {
        Y Invoke(X key) => Delegate.Invoke(key);
    }*/
}

namespace System.Runtime.CompilerServices
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NullableStruct<T> where T : unmanaged
    {
        private static readonly IntPtr classPtr = Il2CppClassPointerStore<Il2CppSystem.Nullable<T>>.NativeClassPtr;

        public T value;
        public bool has_value;

        public NullableStruct(T value)
        {
            this.has_value = true;
            this.value = value;
        }

        public static implicit operator T?(NullableStruct<T> inst)
        {
            if (inst.has_value) { return inst.value; }
            return default;
        }

        public static implicit operator NullableStruct<T>(T? inst)
        {
            if (inst.HasValue) { return new NullableStruct<T>(inst.Value); }
            return default;
        }

        public static unsafe implicit operator NullableStruct<T>(Il2CppSystem.Nullable<T> boxed)
        {
            return *(NullableStruct<T>*)IL2CPP.il2cpp_object_unbox(boxed.Pointer);
        }

        public static unsafe implicit operator Il2CppSystem.Nullable<T>(NullableStruct<T> toBox)
        {
            IntPtr boxed;
            if (toBox.has_value == false) { boxed = toBox.ForceBox(); }
            else { boxed = IL2CPP.il2cpp_value_box(classPtr, (IntPtr)(&toBox)); }
            return new Il2CppSystem.Nullable<T>(boxed);
        }

        private unsafe IntPtr ForceBox()
        {
            IntPtr obj = IL2CPP.il2cpp_object_new(classPtr);
            NullableStruct<T>* boxedValPtr = (NullableStruct<T>*)IL2CPP.il2cpp_object_unbox(obj);
            *boxedValPtr = this;
            return obj;
        }
    }

    public class IsUnmanagedAttribute : Attribute
    {
        public IsUnmanagedAttribute()
        {

        }
    }
}