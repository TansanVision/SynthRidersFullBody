using System.Reflection;

namespace SynthRidersFullBody;

// Generated IL2CPP accessors can change between game versions. Never bind
// optional game members into a method's IL; one missing accessor can stop JIT.
/// <summary>
/// 生成済みIL2CPPアクセサーの欠落や差異を許容する、任意メンバーへのリフレクションアクセスです。
/// </summary>
internal static class OptionalMember
{
    /// <summary>
    /// 公開静的プロパティまたはフィールドを、アクセサーの欠落・例外を吸収して読み取ります。
    /// </summary>
    /// <param name="type">公開静的メンバーを検索する型。</param>
    /// <param name="name">読み取る公開静的プロパティまたはフィールド名。</param>
    /// <returns>読み取った値。メンバーがない場合や取得に失敗した場合はnull。</returns>
    public static object? ReadStatic(Type type, string name)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(null);
            return type.GetField(name, flags)?.GetValue(null);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 公開インスタンスメソッドへbool値を渡して状態変更を試みます。
    /// </summary>
    /// <param name="instance">任意メンバーを持つインスタンス。</param>
    /// <param name="methodName">呼び出す公開インスタンスメソッド名。</param>
    /// <param name="state">メソッドへ渡す有効・無効の状態値。</param>
    /// <returns>対応するメソッドを例外なく呼び出せた場合はtrue。</returns>
    public static bool TrySetBooleanState(object instance, string methodName, bool state)
    {
        try
        {
            var method = instance.GetType().GetMethod(methodName, new[] { typeof(bool) });
            if (method == null) return false;
            method.Invoke(instance, new object[] { state });
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 公開インスタンスプロパティまたはフィールドを、取得時の例外を吸収して読み取ります。
    /// </summary>
    /// <param name="instance">任意メンバーを持つインスタンス。</param>
    /// <param name="name">読み取る公開プロパティまたはフィールド名。</param>
    /// <returns>読み取った値。メンバーがない場合や取得に失敗した場合はnull。</returns>
    public static object? Read(object instance, string name)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var type = instance.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance);
            return type.GetField(name, flags)?.GetValue(instance);
        }
        catch (Exception) { return null; }
    }
}
