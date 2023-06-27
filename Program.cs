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
        static async Task Main(string[] args)
        {
            DateTime DropDate = new DateTime(2022, 7, 23);
            //ValidateBatches(DropDate, true);
            await ValidatePendingPowerMTAOnly();
            //int ValidationSource_ID = 0;//0=Today'sEmails ,1=EmailValidation1,2=Batch PowerMTAOnly, 3=Realtime Emails,10=EmailValidation1 PowerMTAOnly
            //DateTime DropDate = DateTime.Now;
            //int BatchID = 0;
            //if (args.Length > 0) int.TryParse(args[0], out ValidationSource_ID);
            //if (args.Length > 1 && (ValidationSource_ID == 0 || ValidationSource_ID == 3 || ValidationSource_ID == 4)) DateTime.TryParse(args[1], out DropDate);
            //if (args.Length > 1 && (ValidationSource_ID == 2)) int.TryParse(args[1], out BatchID);
            //
            //switch (ValidationSource_ID)
            //{
            //    case 0:
            //        ValidateBatches(DropDate, false);
            //        break;
            //
            //    case 1:
            //        await ValidatePending();
            //        break;
            //
            //    case 2:
            //        await ValidateBatchesPowerMTAOnly(BatchID);
            //        break;
            //
            //    case 3:
            //        ValidateBatches(DropDate, true);
            //        break;
            //
            //    case 4:
            //        await ValidateWeek(DropDate);
            //        break;
            //
            //    case 10:
            //        await ValidatePendingPowerMTAOnly();
            //        break;
            //
            //    default:
            //        ValidateBatches(DropDate, false);
            //        break;
            //}
            //if (!(new[] { 2, 3 }.Contains(ValidationSource_ID)))
            //{
            //    string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;
            //
            //    using (var cleanup = new SqlConnection(connectionString))
            //    {
            //       
            //        cleanup.ExecuteSql("EXEC EmailValidationCleanUp_GetV2");
            //    }
            //}
        }

        public static void ValidateBatches(DateTime DropDate, bool Realtime)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (var connection = new SqlConnection(connectionString))
            {
                IEnumerable<int> Batches = connection.QuerySql<int>("EXEC EmailBatches_GetForValidation @DropDate, @Realtime",
                            new {DropDate = DropDate, Realtime = Realtime }).ToList();

                foreach (int batch in Batches)
                {
                    connection.ExecuteSql("EmailBatchValidationStart_Save @EmailBatch_ID", new { EmailBatch_ID = batch });

                    List<Emails> ValEmails = connection.QuerySql<Emails>("EXEC EmailValidation_GetByBatch @EmailBatch_ID", new { EmailBatch_ID = batch }).ToList();

                    Parallel.ForEach(ValEmails, email =>
                    {
                        if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                        {
                            ValidateWithEASendPowerMTA(email.EmailAddress);
                        }
                        if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                        {
                            ValidateWithPowerMTA(email.EmailAddress);
                        }
                    });

                    foreach (Emails email in ValEmails)
                    {
                        if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                        {
                            ValidateWithEASendPowerMTA(email.EmailAddress);
                        }
                    }

                    connection.ExecuteSql("EmailBatchValidationFinished_Save @EmailBatch_ID", new { EmailBatch_ID = batch });
                }
            }
        }

        public static async Task ValidatePending()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (var batches = new SqlConnection(connectionString))
            {
                IEnumerable<Emails> ValEmails = await batches.QuerySqlAsync<Emails>("EmailValidationPending_GetV2");
                Parallel.ForEach(ValEmails, email =>
                {
                    if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                    {
                        ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                    if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                    {
                        ValidateWithPowerMTA(email.EmailAddress);
                    }
                });
                ValEmails = await batches.QuerySqlAsync<Emails>("EmailValidationPending_GetV2");
                foreach (Emails email in ValEmails)
                {
                    if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                    {
                        ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                }
            }
        }

        public static async Task ValidateWeek(DateTime DropDate)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                var ValBatches = await batches.QuerySqlAsync<WeeklyBatchModel>("EXEC EmailValidationNextWeek_GetV2 @Date", new { Date = DropDate });

                foreach (var email in ValBatches)
                {
                    Console.WriteLine("Batch: " + email.EmailBatch_ID + " Date: " + email.EmailDropDate.ToShortDateString());
                    await ValidateBatchesPowerMTAOnly(email.EmailBatch_ID);
                };
            }
        }

        public static async Task ValidatePendingPowerMTAOnly()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                IEnumerable<Emails> ValEmails = await batches.QuerySqlAsync<Emails>("EXEC EmailValidationPending_GetPMTAOnly");

                // Using a semaphore to limit concurrent tasks
                SemaphoreSlim semaphore = new SemaphoreSlim(400);

                var tasks = ValEmails.Select(async email =>
                {
                    await semaphore.WaitAsync(); 

                    try
                    {
                        ValidateWithPowerMTA(email.EmailAddress);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Wait all tasks to complete
                await Task.WhenAll(tasks);
            }
        }


        public static async Task ValidateBatchesPowerMTAOnly(int BatchID)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batch = new SqlConnection(connectionString))
            {
                var procParams = new { EmailBatch_ID = BatchID };

                await batch.ExecuteSqlAsync("EmailBatchValidationStart_Save", procParams, commandTimeout: 9000);

                List<Emails> ValEmails = (await batch.QuerySqlAsync<Emails>("EmailValidation_GetByBatch", procParams)).ToList();

                var tasks = ValEmails.Select(email => Task.Run(() => ValidateWithPowerMTA(email.EmailAddress))).ToList();

                await Task.WhenAll(tasks);

                await batch.ExecuteSqlAsync("EmailBatchValidationFinished_Save", procParams);
            }
        }

        public static int ValidateWithPowerMTA(string email)
        {
            var client = new RestClient("https://api.sparkpost.com/api/v1/recipient-validation/single/" + email);
            var request = new RestRequest()
            {
                Method = Method.Get
            };
            //client.Timeout = -1;
            
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization", "68b5274962da138495d277b673bfa6");

            RestResponse response = client.Execute(request);
            ValidationRoot Validation = JsonConvert.DeserializeObject<ValidationRoot>(response.Content);
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                try
                {
                    var procParams = new
                    {
                        address = email.ToLower(),
                        isDisposableAddress = Validation.results.is_disposable,
                        isRoleAddress = Validation.results.is_role,
                        reason = Validation.results.reason ?? "",
                        result = Validation.results.result ?? "",
                        DeliveryConfidence = Validation.results.delivery_confidence,
                        did_you_mean = Validation.results.did_you_mean ?? ""
                    };

                    batches.ExecuteSql("EmailValidation_SaveV2", procParams, commandTimeout: 180);
                }
                catch (Exception e)
                {
                    Console.WriteLine("PowerMTA - " + e.Message + " - " + email.ToLower());
                }
            }
            return 1;
        }

        static string ValidateWithMailgun(string email)
        {
            //need to test 
            var clientOptions = new RestClientOptions()
            {
                Authenticator = new HttpBasicAuthenticator("api", "dec256ffd0a59edf3233d6336356995a-9ce9335e-81a9150d"),
                BaseUrl = new Uri("https://api.mailgun.net/v4")
            };
            RestClient client = new RestClient(clientOptions);
            RestRequest request = new RestRequest();
            string result = "";
            request.Resource = "/address/validate";
            request.AddParameter("address", email.ToLower());
            RestResponse respVal = client.Execute(request);
            if (respVal.StatusCode.ToString() == "OK")
            {
                string resultsVal = respVal.Content.ToString();
                dynamic ResultsValOB = JsonConvert.DeserializeObject(resultsVal);
                if (ResultsValOB.reason.Type.ToString() == "Array") ResultsValOB.reason = String.Join(", ", ResultsValOB.reason);
                resultsVal = resultsVal.Replace("[]", "\"\"");
                result = ResultsValOB.result.ToString();
                if (ResultsValOB.reason.ToString() == "mailbox_is_role_address") result = "undeliverable";
                if (ResultsValOB.reason.ToString().Length > 3 && ResultsValOB.reason.ToString().Substring(0, 4).ToLower() == "smtp") result = "undeliverable";
                string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

                using (IDbConnection EmailValidation = new SqlConnection(connectionString))
                {
                    EmailValidation.ExecuteSql(";EXEC EmailValidation_Save @address, @isDisposableAddress, @isRoleAddress, @reason, @result, @risk",
                        new
                        {
                            address = ResultsValOB.address.ToString(),
                            isDisposableAddress = ResultsValOB.is_disposable_address.ToString(),
                            isRoleAddress = ResultsValOB.is_role_address.ToString(),
                            reason = ResultsValOB.reason.ToString(),
                            result = ResultsValOB.result.ToString(),
                            risk = ResultsValOB.risk.ToString()
                        });
                }
            }
            else
            {
                Console.WriteLine("Mailgun - " + respVal.ErrorMessage + " - " + respVal.ResponseStatus);
            }
            return result;
        }

        static void ValidateWithEASend(string email)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;
            try
            {
                SmtpServer vServer = new SmtpServer("");
                SmtpClient oSmtp = new SmtpClient();
                SmtpMail oMail = new SmtpMail("ES-E1582190613-00899-DU956331B9EA29VA-51T11E9DD8A7D591");
                oMail.From = "support@tealeades.com";
                oMail.To = email.ToLower();
                oSmtp.TestRecipients(vServer, oMail);
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
                    using (IDbConnection EmailValidation = new SqlConnection(connectionString))
                    {
                        
                        EmailValidation.ExecuteSql("EXEC EmailValidation_SaveV2 ",
                           new
                           {
                               address = email.ToLower(),
                               isDisposableAddress = "False",
                               isRoleAddress = "False",
                               reason = vep.Message ?? "",
                               result = "undeliverable",
                               DeliveryConfidence = 0,
                               did_you_mean = ""
                           },
                           commandTimeout: 180
                       );
                    }
                }
                else
                {
                    Console.WriteLine("EASend - " + vep.Message + " - " + email.ToLower());
                }
            }
        }

        static void ValidateWithEASendPowerMTA(string email)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;
            try
            {
                SmtpServer vServer = new SmtpServer("");
                SmtpClient oSmtp = new SmtpClient();
                SmtpMail oMail = new SmtpMail("ES-E1582190613-00899-DU956331B9EA29VA-51T11E9DD8A7D591");
                oMail.From = "support@tealeades.com";
                oMail.To = email.ToLower();
                oSmtp.TestRecipients(vServer, oMail);
                ValidateWithPowerMTA(email);
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
                    using (IDbConnection EmailValidation = new SqlConnection(connectionString))
                    {
                        EmailValidation.ExecuteSql("EmailValidation_SaveV2",
                        new
                        {
                            address = email.ToLower(),
                            isDisposableAddress = "False",
                            isRoleAddress = "False",
                            reason = vep.Message ?? "",
                            result = "undeliverable",
                            DeliveryConfidence = 0,
                            did_you_mean = ""
                        },
                        commandTimeout: 180);
                    }
                        
                 
                }
                else
                {
                    ValidateWithPowerMTA(email);
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

