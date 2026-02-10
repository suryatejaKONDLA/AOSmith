using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using AOSmith.Helpers;
using AOSmith.Models;

namespace AOSmith.Services
{
    public class EmailService
    {
        private readonly IDatabaseHelper _dbHelper;

        public EmailService()
        {
            _dbHelper = new DatabaseHelper();
        }

        public EmailService(IDatabaseHelper dbHelper)
        {
            _dbHelper = dbHelper;
        }

        /// <summary>
        /// Write a log entry to App_Data/EmailLog.txt
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, "EmailLog.txt");
                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, entry);
            }
            catch { /* logging itself should never throw */ }
        }

        /// <summary>
        /// Get mail configuration from Mail_Master table
        /// </summary>
        private async Task<MailConfig> GetMailConfigAsync()
        {
            var config = await _dbHelper.QuerySingleAsync<MailConfig>(
                "SELECT TOP 1 * FROM dbo.Mail_Master ORDER BY Mail_SNo");
            return config;
        }

        /// <summary>
        /// Get approver email by approval level
        /// </summary>
        private async Task<List<ApproverInfo>> GetApproversByLevelAsync(int level)
        {
            var query = @"SELECT Login_ID AS UserId, Login_Name AS Name, Login_Email_ID AS Email,
                          Login_Approval_Level AS ApprovalLevel
                          FROM dbo.Login_Master
                          WHERE Login_Is_Approver = 1 AND Login_Approval_Level = @Level AND Login_Active_Flag = 1";
            var parameters = new Dictionary<string, object> { { "@Level", level } };
            var approvers = await _dbHelper.QueryAsync<ApproverInfo>(query, parameters);
            return approvers?.ToList() ?? new List<ApproverInfo>();
        }

        /// <summary>
        /// Get all approvers up to a given level (for rejection notifications)
        /// </summary>
        private async Task<List<ApproverInfo>> GetApproversUpToLevelAsync(int level)
        {
            var query = @"SELECT Login_ID AS UserId, Login_Name AS Name, Login_Email_ID AS Email,
                          Login_Approval_Level AS ApprovalLevel
                          FROM dbo.Login_Master
                          WHERE Login_Is_Approver = 1 AND Login_Approval_Level < @Level
                          AND Login_Active_Flag = 1 ORDER BY Login_Approval_Level";
            var parameters = new Dictionary<string, object> { { "@Level", level } };
            var approvers = await _dbHelper.QueryAsync<ApproverInfo>(query, parameters);
            return approvers?.ToList() ?? new List<ApproverInfo>();
        }

        /// <summary>
        /// Get the document creator's email
        /// </summary>
        private async Task<ApproverInfo> GetDocumentCreatorAsync(int finYear, int recType, int recNumber)
        {
            var query = @"SELECT TOP 1 lm.Login_ID AS UserId, lm.Login_Name AS Name,
                          lm.Login_Email_ID AS Email, ISNULL(lm.Login_Approval_Level, 0) AS ApprovalLevel
                          FROM Stock_Adjustment sa
                          INNER JOIN Login_Master lm ON sa.Stock_Created_ID = lm.Login_ID
                          WHERE sa.Stock_FIN_Year = @FinYear AND sa.Stock_REC_Type = @RecType
                          AND sa.Stock_REC_Number = @RecNumber";
            var parameters = new Dictionary<string, object>
            {
                { "@FinYear", finYear },
                { "@RecType", recType },
                { "@RecNumber", recNumber }
            };
            return await _dbHelper.QuerySingleAsync<ApproverInfo>(query, parameters);
        }

        /// <summary>
        /// Get line item details for a document
        /// </summary>
        private async Task<List<EmailLineItem>> GetLineItemsAsync(int finYear, int recType, int recNumber)
        {
            var query = @"SELECT
                            sa.Stock_REC_SNO AS Sno,
                            RTRIM(sa.Stock_Item_Code) AS ItemCode,
                            RTRIM(sa.Stock_From_Location) AS FromLocation,
                            RTRIM(sa.Stock_To_Location) AS ToLocation,
                            sa.Stock_Qty AS Quantity
                          FROM Stock_Adjustment sa
                          WHERE sa.Stock_FIN_Year = @FinYear AND sa.Stock_REC_Type = @RecType
                          AND sa.Stock_REC_Number = @RecNumber
                          ORDER BY sa.Stock_REC_SNO";
            var parameters = new Dictionary<string, object>
            {
                { "@FinYear", finYear },
                { "@RecType", recType },
                { "@RecNumber", recNumber }
            };
            var items = await _dbHelper.QueryAsync<EmailLineItem>(query, parameters);
            return items?.ToList() ?? new List<EmailLineItem>();
        }

        /// <summary>
        /// Resolve item names and location names from Sage API
        /// </summary>
        private async Task ResolveNamesAsync(List<EmailLineItem> items)
        {
            try
            {
                var sageService = new SageApiService();
                var itemsTask = sageService.GetItemsAsync();
                var locationsTask = sageService.GetLocationsAsync();
                await Task.WhenAll(itemsTask, locationsTask);

                var itemsResponse = await itemsTask;
                var locationsResponse = await locationsTask;

                var itemLookup = new Dictionary<string, SageItem>(StringComparer.OrdinalIgnoreCase);
                if (itemsResponse?.icitems != null)
                {
                    foreach (var item in itemsResponse.icitems)
                    {
                        var key = item.itemno?.Trim();
                        if (!string.IsNullOrEmpty(key) && !itemLookup.ContainsKey(key))
                            itemLookup[key] = item;
                    }
                }

                var locLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (locationsResponse?.locations != null)
                {
                    foreach (var loc in locationsResponse.locations)
                    {
                        var key = loc.location?.Trim();
                        if (!string.IsNullOrEmpty(key) && !locLookup.ContainsKey(key))
                            locLookup[key] = loc.desc?.Trim() ?? "";
                    }
                }

                foreach (var li in items)
                {
                    if (!string.IsNullOrEmpty(li.ItemCode) && itemLookup.TryGetValue(li.ItemCode.Trim(), out var sageItem))
                    {
                        li.ItemName = sageItem.desc?.Trim() ?? "";
                        li.Cost = sageItem.stdcost;
                    }
                    if (!string.IsNullOrEmpty(li.FromLocation) && locLookup.TryGetValue(li.FromLocation.Trim(), out var fromName))
                        li.FromLocationName = fromName;
                    if (!string.IsNullOrEmpty(li.ToLocation) && locLookup.TryGetValue(li.ToLocation.Trim(), out var toName))
                        li.ToLocationName = toName;
                }
            }
            catch
            {
                // If API fails, leave names empty
            }
        }

        // ==================== PUBLIC METHODS ====================

        /// <summary>
        /// Send email to L1 approver when a new stock adjustment record is created
        /// </summary>
        public async Task SendRecordCreatedEmailAsync(int finYear, int recType, int recNumber, string documentReference)
        {
            try
            {
                Log($"SendRecordCreatedEmail START - FinYear={finYear}, RecType={recType}, RecNumber={recNumber}, DocRef={documentReference}");

                var config = await GetMailConfigAsync();
                if (config == null) { Log("ERROR: Mail config not found in Mail_Master"); return; }
                Log($"Mail config loaded: Host={config.Mail_Host}, Port={config.Mail_Port}, From={config.Mail_From_Address}, SSL={config.Mail_SSL_Enabled}");

                var l1Approvers = await GetApproversByLevelAsync(1);
                if (!l1Approvers.Any()) { Log("ERROR: No L1 approvers found (Login_Is_Approver=1, Login_Approval_Level=1, Login_Active_Flag=1)"); return; }
                Log($"L1 approvers found: {string.Join(", ", l1Approvers.Select(a => $"{a.Name} <{a.Email}>"))}");

                var creator = await GetDocumentCreatorAsync(finYear, recType, recNumber);
                var lineItems = await GetLineItemsAsync(finYear, recType, recNumber);
                await ResolveNamesAsync(lineItems);

                var recTypeName = recType == 10 ? "Stock Decrease" : recType == 12 ? "Stock Increase" : "Stock Adjustment";
                var subject = $"New {recTypeName} Pending Approval - {documentReference}";

                var body = BuildHtmlBody(
                    title: $"New {recTypeName} - Pending Your Approval",
                    documentReference: documentReference,
                    recTypeName: recTypeName,
                    createdBy: creator?.Name ?? "Unknown",
                    message: $"A new <strong>{recTypeName}</strong> document has been created and is pending your approval at <strong>Level 1</strong>.",
                    lineItems: lineItems,
                    actionColor: "#004d26",
                    actionLabel: "PENDING APPROVAL"
                );

                foreach (var approver in l1Approvers)
                {
                    if (!string.IsNullOrWhiteSpace(approver.Email))
                    {
                        Log($"Sending record-created email to {approver.Name} <{approver.Email}>");
                        await SendEmailAsync(config, approver.Email, subject, body);
                    }
                    else
                    {
                        Log($"SKIP: Approver {approver.Name} (ID={approver.UserId}) has no email");
                    }
                }
                Log("SendRecordCreatedEmail COMPLETE");
            }
            catch (Exception ex)
            {
                Log($"SendRecordCreatedEmail ERROR: {ex}");
            }
        }

        /// <summary>
        /// Send email to next level approver when current level approves
        /// </summary>
        public async Task SendApprovalEmailToNextLevelAsync(int finYear, int recType, int recNumber,
            string documentReference, int approvedLevel, string approverName)
        {
            try
            {
                Log($"SendApprovalEmailToNextLevel START - DocRef={documentReference}, ApprovedLevel={approvedLevel}");

                var config = await GetMailConfigAsync();
                if (config == null) { Log("ERROR: Mail config not found"); return; }

                int nextLevel = approvedLevel + 1;
                var nextApprovers = await GetApproversByLevelAsync(nextLevel);
                if (!nextApprovers.Any()) { Log($"No approvers found for L{nextLevel} - fully approved"); return; }
                Log($"L{nextLevel} approvers: {string.Join(", ", nextApprovers.Select(a => $"{a.Name} <{a.Email}>"))}");

                var lineItems = await GetLineItemsAsync(finYear, recType, recNumber);
                await ResolveNamesAsync(lineItems);

                var recTypeName = recType == 10 ? "Stock Decrease" : recType == 12 ? "Stock Increase" : "Stock Adjustment";
                var subject = $"{recTypeName} Approved at L{approvedLevel} - Pending Your Approval - {documentReference}";

                var body = BuildHtmlBody(
                    title: $"{recTypeName} - Pending Your Approval (Level {nextLevel})",
                    documentReference: documentReference,
                    recTypeName: recTypeName,
                    createdBy: approverName,
                    message: $"Document <strong>{documentReference}</strong> has been <span style='color:#198754;font-weight:bold;'>Approved</span> at <strong>Level {approvedLevel}</strong> by <strong>{approverName}</strong>.<br/>It is now pending your approval at <strong>Level {nextLevel}</strong>.",
                    lineItems: lineItems,
                    actionColor: "#0d6efd",
                    actionLabel: $"APPROVED AT L{approvedLevel} - PENDING L{nextLevel}"
                );

                foreach (var approver in nextApprovers)
                {
                    if (!string.IsNullOrWhiteSpace(approver.Email))
                    {
                        Log($"Sending approval-next-level email to {approver.Name} <{approver.Email}>");
                        await SendEmailAsync(config, approver.Email, subject, body);
                    }
                }
                Log("SendApprovalEmailToNextLevel COMPLETE");
            }
            catch (Exception ex)
            {
                Log($"SendApprovalEmailToNextLevel ERROR: {ex}");
            }
        }

        /// <summary>
        /// Send email to all previous level approvers and the creator when a document is rejected
        /// </summary>
        public async Task SendRejectionEmailAsync(int finYear, int recType, int recNumber,
            string documentReference, int rejectedAtLevel, string rejectorName, string remarks)
        {
            try
            {
                Log($"SendRejectionEmail START - DocRef={documentReference}, RejectedAtLevel={rejectedAtLevel}");

                var config = await GetMailConfigAsync();
                if (config == null) { Log("ERROR: Mail config not found"); return; }

                // Get all approvers of previous levels + creator
                var recipients = new List<ApproverInfo>();

                // Previous level approvers
                var prevApprovers = await GetApproversUpToLevelAsync(rejectedAtLevel);
                recipients.AddRange(prevApprovers);

                // Document creator
                var creator = await GetDocumentCreatorAsync(finYear, recType, recNumber);
                if (creator != null && !string.IsNullOrWhiteSpace(creator.Email))
                {
                    // Avoid duplicate if creator is also an approver
                    if (!recipients.Any(r => r.UserId == creator.UserId))
                    {
                        recipients.Add(creator);
                    }
                }

                if (!recipients.Any()) { Log("ERROR: No recipients found for rejection email"); return; }
                Log($"Rejection recipients: {string.Join(", ", recipients.Select(r => $"{r.Name} <{r.Email}>"))}");

                var lineItems = await GetLineItemsAsync(finYear, recType, recNumber);
                await ResolveNamesAsync(lineItems);

                var recTypeName = recType == 10 ? "Stock Decrease" : recType == 12 ? "Stock Increase" : "Stock Adjustment";
                var subject = $"{recTypeName} Rejected at L{rejectedAtLevel} - {documentReference}";

                var remarksHtml = !string.IsNullOrWhiteSpace(remarks)
                    ? $"<br/><strong>Remarks:</strong> {System.Web.HttpUtility.HtmlEncode(remarks)}"
                    : "";

                var body = BuildHtmlBody(
                    title: $"{recTypeName} - Rejected",
                    documentReference: documentReference,
                    recTypeName: recTypeName,
                    createdBy: rejectorName,
                    message: $"Document <strong>{documentReference}</strong> has been <span style='color:#dc3545;font-weight:bold;'>Rejected</span> at <strong>Level {rejectedAtLevel}</strong> by <strong>{rejectorName}</strong>.{remarksHtml}",
                    lineItems: lineItems,
                    actionColor: "#dc3545",
                    actionLabel: $"REJECTED AT L{rejectedAtLevel}"
                );

                foreach (var recipient in recipients)
                {
                    if (!string.IsNullOrWhiteSpace(recipient.Email))
                    {
                        Log($"Sending rejection email to {recipient.Name} <{recipient.Email}>");
                        await SendEmailAsync(config, recipient.Email, subject, body);
                    }
                }
                Log("SendRejectionEmail COMPLETE");
            }
            catch (Exception ex)
            {
                Log($"SendRejectionEmail ERROR: {ex}");
            }
        }

        /// <summary>
        /// Send email when all levels are fully approved
        /// </summary>
        public async Task SendFullyApprovedEmailAsync(int finYear, int recType, int recNumber,
            string documentReference, string lastApproverName)
        {
            try
            {
                Log($"SendFullyApprovedEmail START - DocRef={documentReference}");

                var config = await GetMailConfigAsync();
                if (config == null) { Log("ERROR: Mail config not found"); return; }

                var creator = await GetDocumentCreatorAsync(finYear, recType, recNumber);
                if (creator == null || string.IsNullOrWhiteSpace(creator.Email))
                { Log($"ERROR: Creator not found or has no email for FinYear={finYear}, RecType={recType}, RecNumber={recNumber}"); return; }
                Log($"Creator: {creator.Name} <{creator.Email}>");

                var lineItems = await GetLineItemsAsync(finYear, recType, recNumber);
                await ResolveNamesAsync(lineItems);

                var recTypeName = recType == 10 ? "Stock Decrease" : recType == 12 ? "Stock Increase" : "Stock Adjustment";
                var subject = $"{recTypeName} Fully Approved - {documentReference}";

                var body = BuildHtmlBody(
                    title: $"{recTypeName} - Fully Approved",
                    documentReference: documentReference,
                    recTypeName: recTypeName,
                    createdBy: lastApproverName,
                    message: $"Document <strong>{documentReference}</strong> has been <span style='color:#198754;font-weight:bold;'>Fully Approved</span> at all levels. It has been sent to Sage for processing.",
                    lineItems: lineItems,
                    actionColor: "#198754",
                    actionLabel: "FULLY APPROVED"
                );

                await SendEmailAsync(config, creator.Email, subject, body);
                Log("SendFullyApprovedEmail COMPLETE");
            }
            catch (Exception ex)
            {
                Log($"SendFullyApprovedEmail ERROR: {ex}");
            }
        }

        // ==================== HTML BODY BUILDER ====================

        private string BuildHtmlBody(string title, string documentReference, string recTypeName,
            string createdBy, string message, List<EmailLineItem> lineItems,
            string actionColor, string actionLabel)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'/></head>");
            sb.AppendLine("<body style='margin:0; padding:0; font-family: Segoe UI, Arial, sans-serif; background-color: #f4f4f4;'>");

            // Outer container
            sb.AppendLine("<table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4; padding:20px 0;'><tr><td align='center'>");
            sb.AppendLine("<table width='640' cellpadding='0' cellspacing='0' style='background-color:#ffffff; border-radius:8px; overflow:hidden; box-shadow:0 2px 8px rgba(0,0,0,0.1);'>");

            // Header - AO Smith green
            sb.AppendLine("<tr><td style='background-color:#004d26; padding:24px 30px;'>");
            sb.AppendLine("<table width='100%'><tr>");
            sb.AppendLine("<td style='color:#ffffff; font-size:22px; font-weight:bold;'>AO Smith</td>");
            sb.AppendLine($"<td align='right'><span style='background-color:{actionColor}; color:#ffffff; padding:6px 14px; border-radius:4px; font-size:12px; font-weight:bold;'>{actionLabel}</span></td>");
            sb.AppendLine("</tr></table>");
            sb.AppendLine("</td></tr>");

            // Title bar
            sb.AppendLine($"<tr><td style='background-color:#e8f5e9; padding:16px 30px; border-bottom:2px solid #004d26;'>");
            sb.AppendLine($"<div style='font-size:18px; font-weight:bold; color:#004d26;'>{title}</div>");
            sb.AppendLine("</td></tr>");

            // Message
            sb.AppendLine("<tr><td style='padding:20px 30px;'>");
            sb.AppendLine($"<div style='font-size:14px; color:#333; line-height:1.6;'>{message}</div>");
            sb.AppendLine("</td></tr>");

            // Document info
            sb.AppendLine("<tr><td style='padding:0 30px 16px;'>");
            sb.AppendLine("<table width='100%' style='background-color:#f8f9fa; border-radius:6px; padding:12px;'>");
            sb.AppendLine($"<tr><td style='padding:4px 12px; font-size:13px; color:#666; width:150px;'>Document Ref:</td><td style='padding:4px 12px; font-size:13px; font-weight:bold; color:#333;'>{documentReference}</td></tr>");
            sb.AppendLine($"<tr><td style='padding:4px 12px; font-size:13px; color:#666;'>Type:</td><td style='padding:4px 12px; font-size:13px; color:#333;'>{recTypeName}</td></tr>");
            sb.AppendLine($"<tr><td style='padding:4px 12px; font-size:13px; color:#666;'>Date:</td><td style='padding:4px 12px; font-size:13px; color:#333;'>{DateTime.Now:dd/MM/yyyy}</td></tr>");
            sb.AppendLine("</table>");
            sb.AppendLine("</td></tr>");

            // Line items table
            if (lineItems != null && lineItems.Any())
            {
                sb.AppendLine("<tr><td style='padding:0 30px 20px;'>");
                sb.AppendLine("<div style='font-size:14px; font-weight:bold; color:#004d26; margin-bottom:8px;'>Item Details</div>");
                sb.AppendLine("<table width='100%' cellpadding='0' cellspacing='0' style='border-collapse:collapse; font-size:13px;'>");

                // Table header
                sb.AppendLine("<tr style='background-color:#004d26; color:#ffffff;'>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:left; border:1px solid #003d1f;'>Sr</th>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:left; border:1px solid #003d1f;'>Item Code</th>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:left; border:1px solid #003d1f;'>Item Name</th>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:left; border:1px solid #003d1f;'>Location</th>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:right; border:1px solid #003d1f;'>Qty</th>");
                sb.AppendLine("<th style='padding:8px 10px; text-align:right; border:1px solid #003d1f;'>Cost</th>");
                sb.AppendLine("</tr>");

                // Table rows
                int sr = 0;
                decimal totalCost = 0;
                foreach (var item in lineItems)
                {
                    sr++;
                    var bgColor = sr % 2 == 0 ? "#f8f9fa" : "#ffffff";
                    var location = !string.IsNullOrWhiteSpace(item.FromLocation) ? item.FromLocation : item.ToLocation;
                    var locationDisplay = !string.IsNullOrWhiteSpace(item.FromLocationName)
                        ? $"{location} - {item.FromLocationName}"
                        : (!string.IsNullOrWhiteSpace(item.ToLocationName) ? $"{location} - {item.ToLocationName}" : location);
                    var lineCost = item.Cost * item.Quantity;
                    totalCost += lineCost;

                    sb.AppendLine($"<tr style='background-color:{bgColor};'>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6;'>{sr}</td>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6;'>{item.ItemCode}</td>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6;'>{item.ItemName}</td>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6;'>{locationDisplay}</td>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6; text-align:right;'>{item.Quantity:N2}</td>");
                    sb.AppendLine($"<td style='padding:6px 10px; border:1px solid #dee2e6; text-align:right;'>{item.Cost:N2}</td>");
                    sb.AppendLine("</tr>");
                }

                // Total row
                sb.AppendLine("<tr style='background-color:#e8f5e9; font-weight:bold;'>");
                sb.AppendLine($"<td colspan='4' style='padding:8px 10px; border:1px solid #dee2e6; text-align:right;'>Total</td>");
                sb.AppendLine($"<td style='padding:8px 10px; border:1px solid #dee2e6; text-align:right;'>{lineItems.Sum(x => x.Quantity):N2}</td>");
                sb.AppendLine($"<td style='padding:8px 10px; border:1px solid #dee2e6; text-align:right;'>{totalCost:N2}</td>");
                sb.AppendLine("</tr>");

                sb.AppendLine("</table>");
                sb.AppendLine("</td></tr>");
            }

            // Footer
            sb.AppendLine("<tr><td style='background-color:#004d26; padding:16px 30px; text-align:center;'>");
            sb.AppendLine("<div style='color:#ffffff; font-size:12px;'>This is an automated email from AO Smith Stock Adjustment System. Please do not reply.</div>");
            sb.AppendLine("</td></tr>");

            sb.AppendLine("</table>");
            sb.AppendLine("</td></tr></table>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        // ==================== SMTP SEND ====================

        private async Task SendEmailAsync(MailConfig config, string toEmail, string subject, string htmlBody)
        {
            try
            {
                Log($"SMTP connecting: Host={config.Mail_Host}, Port={config.Mail_Port}, SSL={config.Mail_SSL_Enabled}, From={config.Mail_From_Address}, To={toEmail}");
                Log($"Subject: {subject}");

                using (var smtp = new SmtpClient(config.Mail_Host, config.Mail_Port))
                {
                    smtp.Credentials = new NetworkCredential(config.Mail_From_Address, config.Mail_From_Password);
                    smtp.EnableSsl = config.Mail_SSL_Enabled;
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.Timeout = 30000; // 30 seconds

                    using (var message = new MailMessage())
                    {
                        message.From = new MailAddress(config.Mail_From_Address,
                            !string.IsNullOrWhiteSpace(config.Mail_Display_Name) ? config.Mail_Display_Name : "AO Smith");
                        message.To.Add(toEmail);
                        message.Subject = subject;
                        message.Body = htmlBody;
                        message.IsBodyHtml = true;

                        await smtp.SendMailAsync(message);
                        Log($"EMAIL SENT SUCCESSFULLY to {toEmail}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"EMAIL SEND FAILED to {toEmail}: {ex}");
            }
        }
    }

    // ==================== HELPER MODELS ====================

    public class ApproverInfo
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int ApprovalLevel { get; set; }
    }

    public class EmailLineItem
    {
        public int Sno { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public string FromLocation { get; set; }
        public string FromLocationName { get; set; }
        public string ToLocation { get; set; }
        public string ToLocationName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Cost { get; set; }
    }
}
