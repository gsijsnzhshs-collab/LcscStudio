using System;

namespace LcscLoader
{
    // 立创接口错误分类：让 UI 能区分“断网 / 格式变了 / 器件不存在”。
    internal class EeNetworkError : Exception
    {
        public EeNetworkError(string message, Exception inner = null) : base(message, inner) { }
    }

    internal sealed class EeFormatError : Exception
    {
        public EeFormatError(string message, Exception inner = null) : base(message, inner) { }
    }

    internal sealed class EeNotFoundError : Exception
    {
        public EeNotFoundError(string message) : base(message) { }
    }

    internal static class EeErrors
    {
        // 面向用户的简短说明；完整异常仍写 plugin.log。
        public static string Describe(Exception error)
        {
            if (error is EeNotFoundError) return "未找到该器件（编号错误或已下架）";
            if (error is EeNetworkError) return "网络错误，请检查网络后重试";
            if (error is EeFormatError) return "接口数据格式变化，暂时无法解析";
            return error == null ? "未知错误" : error.Message;
        }
    }
}
