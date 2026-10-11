using System.Globalization;

namespace CodexHebdo;

// NumericUpDown normally clamps intermediate input: typing 12 can turn the first
// '1' into 8, then the appended '2' into 82 -> 20. Keep incomplete input local.
internal sealed class SizeInput : NumericUpDown
{
    private bool Complete => decimal.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
        && value >= Minimum && value <= Maximum;

    protected override void ValidateEditText()
    {
        if (Complete) base.ValidateEditText();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (!Complete) { UserEdit = false; UpdateEditText(); }
    }
}
