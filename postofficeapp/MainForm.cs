using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace PostOfficeApp;

internal sealed class MainForm : Form
{
    private const string ApiEndpoint = "https://localhost:44390/api/getcheckinfo";
    private const string UpdateApiEndpoint = "https://localhost:44390/api/updatecheckstatus";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly HttpClient _httpClient = new();
    private readonly TextBox _bpNumberTextBox = new() { Width = 150 };
    private readonly TextBox _checkNumberTextBox = new() { Width = 150 };
    private readonly TextBox _nisNumberTextBox = new() { Width = 180 };
    private readonly Button _searchButton = new() { Text = "Search", AutoSize = true };
    private readonly Button _cashButton = new() { Text = "Cash selected cheque", AutoSize = true, Enabled = false };
    private readonly DataGridView _checksGrid = new();
    private readonly TextBox _responseTextBox = new();

    public MainForm()
    {
        Text = "Post Office Cheque Lookup";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 550);
        Size = new Size(1050, 700);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        Controls.Add(layout);

        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 10)
        };
        searchPanel.Controls.Add(new Label { Text = "BP Number:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        searchPanel.Controls.Add(_bpNumberTextBox);
        searchPanel.Controls.Add(new Label { Text = "Check Number:", AutoSize = true, Margin = new Padding(12, 8, 3, 0) });
        searchPanel.Controls.Add(_checkNumberTextBox);
        searchPanel.Controls.Add(_searchButton);
        layout.Controls.Add(searchPanel, 0, 0);

        _checksGrid.Dock = DockStyle.Fill;
        _checksGrid.AllowUserToAddRows = false;
        _checksGrid.AllowUserToDeleteRows = false;
        _checksGrid.ReadOnly = true;
        _checksGrid.MultiSelect = false;
        _checksGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _checksGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _checksGrid.RowHeadersVisible = false;
        _checksGrid.Columns.Add("BusPartner", "BusPartner");
        _checksGrid.Columns.Add("Name", "Name");
        _checksGrid.Columns.Add("NationalId", "National ID");
        _checksGrid.Columns.Add("ChequeNumber", "Cheque Number");
        _checksGrid.Columns.Add("Status", "Status");
        _checksGrid.Columns.Add("AmountPaid", "Amount Paid");
        layout.Controls.Add(_checksGrid, 0, 1);

        var cashPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 10)
        };
        cashPanel.Controls.Add(new Label { Text = "NIS Number:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        cashPanel.Controls.Add(_nisNumberTextBox);
        cashPanel.Controls.Add(_cashButton);
        layout.Controls.Add(cashPanel, 0, 2);

        _responseTextBox.Dock = DockStyle.Fill;
        _responseTextBox.Multiline = true;
        _responseTextBox.ReadOnly = true;
        _responseTextBox.ScrollBars = ScrollBars.Vertical;
        _responseTextBox.Font = new Font(FontFamily.GenericMonospace, 9);
        layout.Controls.Add(_responseTextBox, 0, 3);

        _searchButton.Click += async (_, _) => await SearchAsync();
        _cashButton.Click += async (_, _) => await CashSelectedChequeAsync();
        _checksGrid.SelectionChanged += (_, _) => UpdateCashControls();
        _checkNumberTextBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                await SearchAsync();
            }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task SearchAsync()
    {
        var bpNumber = _bpNumberTextBox.Text.Trim();
        var checkNumber = _checkNumberTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(bpNumber) || string.IsNullOrWhiteSpace(checkNumber))
        {
            MessageBox.Show(this, "Enter both a BP number and a check number.", "Missing search details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _searchButton.Enabled = false;
        _cashButton.Enabled = false;
        _checksGrid.Rows.Clear();
        _responseTextBox.Clear();

        try
        {
            var lookupRequest = new { bp = bpNumber, check = checkNumber };
            using var response = await _httpClient.PostAsJsonAsync(ApiEndpoint, lookupRequest, JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                ShowResult($"Lookup API request failed ({(int)response.StatusCode} {response.ReasonPhrase}):\r\n{errorBody}");
                return;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(responseStream);
            if (!document.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Array)
            {
                ShowResult("The API response did not contain a valid payload array.");
                return;
            }

            var statusMessages = new List<string>();
            foreach (var item in payload.EnumerateArray())
            {
                var chequeNumber = GetValue(item, "Cheque Number");
                var status = GetValue(item, "Status");
                var rowIndex = _checksGrid.Rows.Add(
                    GetValue(item, "BusPartner"),
                    GetValue(item, "Name"),
                    GetValue(item, "National ID"),
                    chequeNumber,
                    status,
                    GetValue(item, "Amount Paid"));
                _checksGrid.Rows[rowIndex].Tag = item.Clone();

                var statusMessage = GetStatusMessage(status);
                if (statusMessage is not null)
                {
                    statusMessages.Add($"Cheque {chequeNumber}: {statusMessage}");
                }
            }

            var resultMessage = payload.GetArrayLength() == 0
                ? "No check information found."
                : $"Found {payload.GetArrayLength()} cheque(s). Select an open cheque to cash it.";
            if (statusMessages.Count > 0)
            {
                resultMessage += Environment.NewLine + string.Join(Environment.NewLine, statusMessages);
            }

            ShowResult(resultMessage);

            if (_checksGrid.Rows.Count > 0)
            {
                _checksGrid.Rows[0].Selected = true;
                UpdateCashControls();
            }
        }
        catch (HttpRequestException exception)
        {
            ShowResult($"Could not contact the API: {exception.Message}");
        }
        catch (JsonException exception)
        {
            ShowResult($"Could not parse the API response: {exception.Message}");
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    private async Task CashSelectedChequeAsync()
    {
        if (_checksGrid.CurrentRow?.Tag is not JsonElement item)
        {
            MessageBox.Show(this, "Select a cheque first.", "No cheque selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!string.Equals(GetValue(item, "Status"), "Open", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Only open cheques can be cashed.", "Cheque not open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var nisNumber = _nisNumberTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(nisNumber))
        {
            MessageBox.Show(this, "Enter an NIS number before cashing the cheque.", "NIS number required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nisNumberTextBox.Focus();
            return;
        }

        if (!TryGetDecimal(item, "Amount Paid", out var chequeAmount))
        {
            MessageBox.Show(this, "Amount Paid is missing or invalid; the cheque cannot be cashed.", "Invalid cheque amount", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var chequeNumber = GetValue(item, "Cheque Number");
        if (MessageBox.Show(this, $"Cash cheque {chequeNumber}?", "Confirm cheque cashing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var cashRequest = new
        {
            chequeNumber,
            postOfficeCode = GeneratePostOfficeCode(),
            nationalRegistrationNumber = GetValue(item, "National ID"),
            chequeAmount,
            NISNumber = nisNumber,
            cashedDate = DateOnly.FromDateTime(DateTime.Today)
        };

        _cashButton.Enabled = false;
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(UpdateApiEndpoint, cashRequest, JsonOptions);
            var responseBody = await response.Content.ReadAsStringAsync();
            var label = response.IsSuccessStatusCode ? "Update API response" : "Update API request failed";
            ShowResult($"{label} ({(int)response.StatusCode} {response.ReasonPhrase}):\r\n{(string.IsNullOrWhiteSpace(responseBody) ? "(empty response)" : responseBody)}");
        }
        catch (HttpRequestException exception)
        {
            ShowResult($"Could not contact the update API: {exception.Message}");
        }
        finally
        {
            UpdateCashControls();
        }
    }

    private void UpdateCashControls()
    {
        if (_checksGrid.CurrentRow?.Tag is not JsonElement item)
        {
            _cashButton.Enabled = false;
            _nisNumberTextBox.Clear();
            return;
        }

        _cashButton.Enabled = string.Equals(GetValue(item, "Status"), "Open", StringComparison.OrdinalIgnoreCase);
        var nisNumber = GetValue(item, "NISNumber");
        _nisNumberTextBox.Text = string.IsNullOrWhiteSpace(nisNumber) ? GetValue(item, "NIS Number") : nisNumber;
    }

    private void ShowResult(string message)
    {
        _responseTextBox.Text = message;
    }

    private static string? GetStatusMessage(string status)
    {
        if (string.Equals(status, "Cashed", StringComparison.OrdinalIgnoreCase))
        {
            return "This cheque has already been cashed and cannot be cashed again.";
        }

        if (string.Equals(status, "Void", StringComparison.OrdinalIgnoreCase))
        {
            return "This cheque is void and cannot be cashed.";
        }

        return null;
    }

    private static string GetValue(JsonElement item, string propertyName)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool TryGetDecimal(JsonElement item, string propertyName, out decimal result)
    {
        result = 0;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDecimal(out result);
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static string GeneratePostOfficeCode()
    {
        var digits = string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(10)));
        var suffix = (char)('A' + RandomNumberGenerator.GetInt32(26));
        return $"GPS{digits}{suffix}";
    }
}
