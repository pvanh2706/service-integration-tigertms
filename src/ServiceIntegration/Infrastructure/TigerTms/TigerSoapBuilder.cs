using System.Security;
using System.Text;

namespace ServiceIntegration.Infrastructure.TigerTms;

public static class TigerSoapBuilder
{
    public static string EscapeInnerXml(string xml)
        => SecurityElement.Escape(xml) ?? string.Empty;

    public static string WrapCheckIn(string escapedInnerXml)
    {
        return
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">\n" +
            "  <soap:Body>\n" +
            "    <checkIn xmlns=\"http://tigergenericinterface.org/\">\n" +
            "      <XMLString>" + escapedInnerXml + "</XMLString>\n" +
            "    </checkIn>\n" +
            "  </soap:Body>\n" +
            "</soap:Envelope>";
    }

    public static string BuildCheckInInnerXml(
        string resno,
        string site,
        string room,
        string wsuserkey,
        IDictionary<string, string?> optionalNodes)
    {
        var sb = new StringBuilder();

        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<checkinresults resno=\"" + EscapeAttr(resno) + "\">");

        sb.Append("<site>" + EscapeNode(site) + "</site>");
        sb.Append("<room>" + EscapeNode(room) + "</room>");

        foreach (var kv in optionalNodes)
        {
            if (kv.Value == null) continue;
            sb.Append("<" + kv.Key + ">" + EscapeNode(kv.Value) + "</" + kv.Key + ">");
        }

        sb.Append("<wsuserkey>" + EscapeNode(wsuserkey) + "</wsuserkey>");
        sb.Append("</checkinresults>");

        return sb.ToString();
    }

    private static string EscapeNode(string value)
        => SecurityElement.Escape(value) ?? string.Empty;

    private static string EscapeAttr(string value)
        => value.Replace("\"", "&quot;");
}