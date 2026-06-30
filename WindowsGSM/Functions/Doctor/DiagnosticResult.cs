namespace WindowsGSM.Functions.Doctor
{
    public enum DiagStatus { Ok, Warn, Fail, Info, Skip }

    /// <summary>One line of a server's health report.</summary>
    public class DiagnosticResult
    {
        public string Check { get; set; } = "";
        public DiagStatus Status { get; set; } = DiagStatus.Info;
        public string Detail { get; set; } = "";

        public DiagnosticResult() { }
        public DiagnosticResult(string check, DiagStatus status, string detail)
        {
            Check = check; Status = status; Detail = detail;
        }
    }
}
