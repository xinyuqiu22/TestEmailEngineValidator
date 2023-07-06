using EASendMail;
using Newtonsoft.Json;
using Insight.Database;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Microsoft.Data.SqlClient;
using System.Data;

namespace TestEmailEngineValidator
{
    class Program
    {
        static string DataCenterEmailEngine = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;
        static async Task Main(string[] args)
        {
            int ValidationSource_ID = 0;//0=Today'sEmails ,1=EmailValidation1,2=Batch PowerMTAOnly, 3=Realtime Emails 4 = Validate next week emails,10=EmailValidation1 PowerMTAOnly
            DateTime DropDate = DateTime.Now;
            int BatchID = 0;
            if (args.Length > 0) _ = int.TryParse(args[0], out ValidationSource_ID);
            if (args.Length > 1 && (ValidationSource_ID == 0 || ValidationSource_ID == 3 || ValidationSource_ID == 4)) _ = DateTime.TryParse(args[1], out DropDate);
            if (args.Length > 1 && (ValidationSource_ID == 2)) _ = int.TryParse(args[1], out BatchID);

            switch (ValidationSource_ID)
            {
                case 0:
                    await ValidateBatches(DropDate, false);
                    break;
            
                case 1:
                    await ValidatePending();
                    break;
            
                case 2:
                    await ValidateBatchesPowerMTAOnly(BatchID);
                    break;
            
                case 3:
                    await ValidateBatches(DropDate, true);
                    break;
            
                case 4:
                    await ValidateWeek(DropDate);
                    break;
            
                case 10:
                    await ValidatePendingPowerMTAOnly();
                    break;
            
                default:
                    await ValidateBatches(DropDate, false);
                    break;
            }
            if (!(new[] { 2, 3 }.Contains(ValidationSource_ID)))
            {
                using var cleanup = new SqlConnection(DataCenterEmailEngine);
                cleanup.Execute("EmailValidationCleanUp_GetV2", commandTimeout: 6000);
            }
        }

        public static async Task ValidateBatches(DateTime DropDate, bool Realtime)
        {
            using var connection = new SqlConnection(DataCenterEmailEngine);
            IEnumerable<int> Batches = connection.Query<int>("EmailBatches_GetForValidation",new { DropDate, Realtime }).ToList();

            foreach (int batch in Batches)
            {
                connection.Execute("EmailBatchValidationStart_Save",new { EmailBatch_ID = batch });

                IEnumerable<Emails> ValEmails = connection.Query<Emails>("EmailValidation_GetByBatch",new { EmailBatch_ID = batch });

                Parallel.ForEach(ValEmails, async email =>
                {
                    if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                    {
                        _ = ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                    if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                    {
                        _ = ValidateWithPowerMTA(email.EmailAddress);
                    }
                    Console.WriteLine(email.EmailServiceProvider_ID);
                });

                foreach (Emails email in ValEmails)
                {
                    if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                    {
                        _ = ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                }

                connection.Execute("EmailBatchValidationFinished_Save", new { EmailBatch_ID = batch });
            }
        }

        public static async Task ValidatePending()
        {
            using var batches = new SqlConnection(DataCenterEmailEngine);
            IEnumerable<Emails> ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetV2", commandTimeout: 180);

            Parallel.ForEach(ValEmails, async email =>
            {
                if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                {
                    _ = ValidateWithEASendPowerMTA(email.EmailAddress);
                }
                if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                {
                    _ = ValidateWithPowerMTA(email.EmailAddress);
                }
                Console.WriteLine(email.EmailServiceProvider_ID);
            });

            ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetV2", commandTimeout: 180);
            foreach (Emails email in ValEmails)
            {
                if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                {
                    _ = ValidateWithEASendPowerMTA(email.EmailAddress);
                }
            }
        }

        public static async Task ValidateWeek(DateTime DropDate)
        {
            using IDbConnection batches = new SqlConnection(DataCenterEmailEngine);

            var ValBatches = await batches.QueryAsync<WeeklyBatchModel>("EmailValidationNextWeek_GetV2",new { Date = DropDate }, commandTimeout: 180);

            var tasks = ValBatches.Select(async email =>
            {
                Console.WriteLine("Batch: " + email.EmailBatch_ID + " Date: " + email.EmailDropDate.ToShortDateString());
                await ValidateBatchesPowerMTAOnly(email.EmailBatch_ID);
            });

            await Task.WhenAll(tasks);
        }

        public static async Task ValidatePendingPowerMTAOnly()
        {
            using IDbConnection batches = new SqlConnection(DataCenterEmailEngine);
            IEnumerable<Emails> ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetPMTAOnly", commandTimeout: 180);

            // Using a semaphore to limit concurrent tasks
            SemaphoreSlim semaphore = new(20);

            var tasks = ValEmails.Select(async email =>
            {
                await semaphore.WaitAsync();

                try
                {
                    _ = ValidateWithPowerMTA(email.EmailAddress);
                }
                finally
                {
                    semaphore.Release();
                }
                Console.WriteLine(email.EmailAddress);
            });

            // Wait all tasks to complete
            await Task.WhenAll(tasks);
        }


        public static async Task ValidateBatchesPowerMTAOnly(int BatchID)
        {
            using IDbConnection batch = new SqlConnection(DataCenterEmailEngine);

            await batch.ExecuteAsync("EmailBatchValidationStart_Save",new { EmailBatch_ID = BatchID });

            IEnumerable<Emails> ValEmails = await batch.QueryAsync<Emails>("EmailValidation_GetByBatch",new { EmailBatch_ID = BatchID }, commandTimeout: 180);

            var tasks = ValEmails.Select(email => Task.Run(() => ValidateWithPowerMTA(email.EmailAddress))).ToList();

            await Task.WhenAll(tasks);

            await batch.ExecuteAsync("EmailBatchValidationFinished_Save",new { EmailBatch_ID = BatchID });
        }

        public static async Task<int> ValidateWithPowerMTA(string email)
        {
            var options = new RestClientOptions()
            {
                //Authenticator = new HttpBasicAuthenticator("api", EngineSetting.OutboundPP),
                BaseUrl = new Uri("https://api.sparkpost.com/api/v1/recipient-validation/single/" + Uri.EscapeDataString(email)),
                MaxTimeout = 1800000000
            };
            RestClient client = new(options);
            RestRequest request = new()
            {
                Method = Method.Get
            };
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization", "68b5274962da556a4ac7da138495d277b673bfa6");

            RestResponse response = client.Execute(request);
            ValidationRoot? Validation = JsonConvert.DeserializeObject<ValidationRoot>(response.Content ?? "");
            if (Validation != null)
            {
                using IDbConnection batches = new SqlConnection(DataCenterEmailEngine);
                try
                {
                    _ = await batches.ExecuteAsync("EmailValidation_SaveV2 ",
                        new
                        {
                            address = email.ToLower(),
                            isDisposableAddress = Validation.results.is_disposable,
                            isRoleAddress = Validation.results.is_role,
                            reason = Validation.results.reason ?? "",
                            result = Validation.results.result ?? "",
                            DeliveryConfidence = Validation.results.delivery_confidence,
                            did_you_mean = Validation.results.did_you_mean ?? ""
                        });
                }
                catch (Exception e)
                {
                    Console.WriteLine("PowerMTA - " + e.Message + " - " + email.ToLower());
                }
                return 1;
            }
            else  return 0;
        }

        static async Task ValidateWithEASendPowerMTA(string email)
        {
            try
            {
                SmtpServer vServer = new("");
                SmtpClient oSmtp = new();
                SmtpMail oMail = new("ES-E1582190613-00899-DU956331B9EA29VA-51T11E9DD8A7D591")
                {
                    From = "support@tealeades.com",
                    To = email.ToLower()
                };
                //next line will cause error if the email fails and drop into the catch bracket
                oSmtp.TestRecipients(vServer, oMail);
                //if the line above doesn't fail, we want to go to ValidateWithPowerMTA
                await ValidateWithPowerMTA(email);
            }
            catch (Exception vep)
            {
                if (!vep.Message.ToLower().Contains("too many") &&
                    !vep.Message.ToLower().Contains("blocked") &&
                    !vep.Message.ToLower().Contains("delayed") &&
                    !vep.Message.ToLower().Contains("refused") &&
                    !vep.Message.ToLower().Contains("exceeded") &&
                    !vep.Message.ToLower().Contains("try again") &&
                    !vep.Message.Contains("74.118.137.7"))
                {
                    using IDbConnection EmailValidation = new SqlConnection(DataCenterEmailEngine);
                    await EmailValidation.ExecuteAsync("EmailValidation_SaveV2",
                        new
                        {
                            address = email.ToLower(),
                            isDisposableAddress = "False",
                            isRoleAddress = "False",
                            reason = vep.Message ?? "",
                            result = "undeliverable",
                            DeliveryConfidence = 0,
                            did_you_mean = ""
                        });
                }
                else
                {
                    await ValidateWithPowerMTA(email);
                    Console.WriteLine("EASend - " + vep.Message + " - " + email.ToLower());
                }
            }
        }


        public class Emails
        {
            public int EmailServiceProvider_ID { get; set; }
            public string EmailAddress { get; set; }
        }
        public class Results
        {
            public bool valid { get; set; }
            public string result { get; set; }
            public string reason { get; set; }
            public bool is_role { get; set; }
            public bool is_disposable { get; set; }
            public bool is_free { get; set; }
            public int delivery_confidence { get; set; }
            public string did_you_mean { get; set; }
        }

        public class ValidationRoot
        {
            public Results results { get; set; }
        }

        public class BatchModel
        {
            public int EmailBatch_ID { get; set; }
            public string BusinessName { get; set; }
            public string FromEmailAddress { get; set; }
            public string ReplyToEmailAddress { get; set; }
            public string SubjectLine { get; set; }
            public string TemplateURL { get; set; }
            public string Unsubscribe { get; set; }
            public int FailedEmailCount { get; set; }
            public bool ClickTracking { get; set; }
        }
        public class WeeklyBatchModel
        {
            public int EmailBatch_ID { get; set; }
            public DateTime EmailDropDate { get; set; }
        }
    }
}

