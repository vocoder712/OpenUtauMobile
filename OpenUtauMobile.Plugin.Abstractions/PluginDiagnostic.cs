namespace OpenUtauMobile.Plugin.Abstractions
{
    /// <summary>诊断的展示级别。</summary>
    public enum PluginDiagnosticSeverity
    {
        Information,
        Warning,
        Error,
    }

    /// <summary>可展示的诊断；代码稳定，消息由插件按当前文化信息生成。</summary>
    public sealed record PluginDiagnostic(
        string Code,
        string Message,
        PluginDiagnosticSeverity Severity = PluginDiagnosticSeverity.Error,
        string? Location = null,
        string? FieldId = null);

    /// <summary>宿主提供的日志入口，自动关联插件及会话身份。</summary>
    public interface IPluginLog
    {
        /// <summary>记录诊断和可选原始异常，不替代界面中的错误展示。</summary>
        void Write(PluginDiagnostic diagnostic, Exception? exception = null);
    }
}
