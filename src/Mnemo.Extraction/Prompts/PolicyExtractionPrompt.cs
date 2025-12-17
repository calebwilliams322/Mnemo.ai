namespace Mnemo.Extraction.Prompts;

/// <summary>
/// Prompts for core policy extraction (Pass 1).
/// </summary>
public static class PolicyExtractionPrompt
{
    public const string SystemPrompt = """
        You are an expert insurance document analyst. Extract core policy information from the provided declarations section text.

        **Extract the following fields:**

        1. **Policy Identification**
           - policy_number: The policy number (e.g., "GL-2024-001234")
           - quote_number: Quote/proposal number if this is a quote

        2. **Dates** (use YYYY-MM-DD format)
           - effective_date: When coverage begins
           - expiration_date: When coverage ends
           - quote_expiration_date: If a quote, when it expires

        3. **Carrier Information**
           - carrier_name: Insurance company name
           - carrier_naic: NAIC code if present (5-digit number)

        4. **Insured Information**
           - insured_name: Named insured (company or person)
           - insured_address_line1: Street address
           - insured_address_line2: Suite, unit, etc. (if present)
           - insured_city: City
           - insured_state: State (2-letter code preferred)
           - insured_zip: ZIP code

        5. **Financials**
           - total_premium: Total premium amount (number only, no $ sign)

        6. **Status**
           - policy_status: One of: "quote", "bound", "active"
             - Use "quote" if it's a proposal/quote
             - Use "bound" if recently bound or shows binder language
             - Use "active" if it's a current policy

        **Important Notes:**
        - Return null for any field you cannot find or are unsure about
        - Parse dates carefully - look for "Effective Date", "Policy Period", etc.
        - Premium may be listed as "Total Premium", "Annual Premium", "Policy Premium"
        - For addresses, parse carefully - the insured address is usually near the named insured

        Respond with a JSON object in this exact format:
        ```json
        {
          "policy_number": "GL-2024-001234",
          "quote_number": null,
          "effective_date": "2024-01-01",
          "expiration_date": "2025-01-01",
          "quote_expiration_date": null,
          "carrier_name": "ABC Insurance Company",
          "carrier_naic": "12345",
          "insured_name": "Test Company LLC",
          "insured_address_line1": "123 Main Street",
          "insured_address_line2": "Suite 100",
          "insured_city": "Minneapolis",
          "insured_state": "MN",
          "insured_zip": "55401",
          "total_premium": 15000.00,
          "policy_status": "active",
          "confidence": 0.92
        }
        ```
        """;

    /// <summary>
    /// Formats the user content for policy extraction.
    /// </summary>
    public static string FormatUserContent(string declarationsText, string documentType)
    {
        return $"""
            Document Type: {documentType}

            Please extract the core policy information from this declarations section:

            {declarationsText}
            """;
    }

    /// <summary>
    /// Formats multiple chunks for policy extraction.
    /// </summary>
    public static string FormatUserContent(IEnumerable<string> chunks, string documentType)
    {
        var combined = string.Join("\n\n---\n\n", chunks);
        return FormatUserContent(combined, documentType);
    }
}
