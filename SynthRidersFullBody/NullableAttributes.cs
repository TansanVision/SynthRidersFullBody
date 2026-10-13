// Unity's generated IL2CPP CoreModule contains stub nullable attributes without
// constructors. Define the compiler metadata attributes locally for this mod.
namespace System.Runtime.CompilerServices;

/// <summary>
/// IL2CPP参照DLLの不足を補う、コンパイラー用のnull許容性メタデータ属性です。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Event | AttributeTargets.Field |
                AttributeTargets.GenericParameter | AttributeTargets.Parameter |
                AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Struct)]
internal sealed class NullableAttribute : Attribute
{
    /// <summary>
    /// コンパイラーが出力するnull許容性フラグを受け取る属性コンストラクターです。
    /// </summary>
    /// <param name="flag">コンパイラーが生成するnull許容性のフラグ。この互換属性では値を保持しません。</param>
    /// <remarks>IL2CPP参照DLLには対応するコンストラクターがないため、コンパイル時のメタデータ用に定義しています。</remarks>
    public NullableAttribute(byte flag) { }
    /// <summary>
    /// コンパイラーが出力するnull許容性フラグを受け取る属性コンストラクターです。
    /// </summary>
    /// <param name="flags">コンパイラーが生成するnull許容性のフラグ列。この互換属性では値を保持しません。</param>
    /// <remarks>IL2CPP参照DLLには対応するコンストラクターがないため、コンパイル時のメタデータ用に定義しています。</remarks>
    public NullableAttribute(byte[] flags) { }
}

/// <summary>
/// コンパイラーのnull許容コンテキストを表す互換属性です。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Interface |
                AttributeTargets.Method | AttributeTargets.Struct)]
internal sealed class NullableContextAttribute : Attribute
{
    /// <summary>
    /// コンパイラーが出力するコンテキストフラグを受け取ります。
    /// </summary>
    /// <param name="flag">コンパイラーが生成するnull許容性のフラグ。この互換属性では値を保持しません。</param>
    public NullableContextAttribute(byte flag) { }
}
