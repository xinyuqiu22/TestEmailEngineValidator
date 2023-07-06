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
            int ValidationSource_ID = 0;//0=Today'sEmails ,1=EmailValidation1,2=Batch PowerMTAOnly, 3=Realtime Emails,10=EmailValidation1 PowerMTAOnly
            DateTime DropDate = DateTime.Now;
            int BatchID = 0;
            if (args.Length > 0) int.TryParse(args[0], out ValidationSource_ID);
            if (args.Length > 1 && (ValidationSource_ID == 0 || ValidationSource_ID == 3 || ValidationSource_ID == 4)) DateTime.TryParse(args[1], out DropDate);
            if (args.Length > 1 && (ValidationSource_ID == 2)) int.TryParse(args[1], out BatchID);
            //Console.WriteLine(ValidationSource_ID);
            //Console.WriteLine(BatchID);
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
                string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;
            
                using (var cleanup = new SqlConnection(connectionString))
                {
                   
                    cleanup.Execute("EmailValidationCleanUp_GetV2", 
                        commandType: CommandType.StoredProcedure);
                }
            }
        }

        public static async Task ValidateBatches(DateTime DropDate, bool Realtime)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (var connection = new SqlConnection(connectionString))
            {
                IEnumerable<int> Batches = connection.Query<int>("EmailBatches_GetForValidation",
                    new { DropDate = DropDate, Realtime = Realtime },

                foreach (int batch in Batches)
                {
                    connection.Execute("EmailBatchValidationStart_Save", 
                        new { EmailBatch_ID = batch });

                    IEnumerable<Emails> ValEmails = connection.Query<Emails>("EmailValidation_GetByBatch",
                        new { EmailBatch_ID = batch },commandType: CommandType.StoredProcedure);
                        commandType: CommandType.StoredProcedure);

                    Parallel.ForEach(ValEmails, async email =>
                    {
                        if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                        {
                            await ValidateWithEASendPowerMTA(email.EmailAddress);
                        }
                        if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                        {
                            await ValidateWithPowerMTA(email.EmailAddress);
                        }
                        Console.WriteLine(email.EmailServiceProvider_ID);
                    });

                    foreach (Emails email in ValEmails)
                    {
                        if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                        {
                            await ValidateWithEASendPowerMTA(email.EmailAddress);
                        }
                    }

                    connection.Execute("EmailBatchValidationFinished_Save", new { EmailBatch_ID = batch });
                }
            }
        }

        public static async Task ValidatePending()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (var batches = new SqlConnection(connectionString))
            {
                IEnumerable<Emails> ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetV2", commandType: CommandType.StoredProcedure);
                    commandType: CommandType.StoredProcedure);

                Parallel.ForEach(ValEmails, async email =>
                {
                    if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
                    {
                        await ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                    if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
                    {
                        await ValidateWithPowerMTA(email.EmailAddress);
                    }
                    Console.WriteLine(email.EmailServiceProvider_ID);
                });

                ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetV2",
                    commandType: CommandType.StoredProcedure);

                foreach (Emails email in ValEmails)
                {
                    if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
                    {
                        await ValidateWithEASendPowerMTA(email.EmailAddress);
                    }
                }

            }
        }

        public static async Task ValidateWeek(DateTime DropDate)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                //batches.Open();

                var ValBatches = await batches.QueryAsync<WeeklyBatchModel>("EmailValidationNextWeek_GetV2",
                    new { Date = DropDate },
                    commandType: CommandType.StoredProcedure);
                );

                var tasks = ValBatches.Select(async email =>
                {
                    Console.WriteLine("Batch: " + email.EmailBatch_ID + " Date: " + email.EmailDropDate.ToShortDateString());
                    await ValidateBatchesPowerMTAOnly(email.EmailBatch_ID);
                });

                await Task.WhenAll(tasks);
                //batches.Close();
            }
        }

        public static async Task ValidatePendingPowerMTAOnly()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                IEnumerable<Emails> ValEmails = await batches.QueryAsync<Emails>("EmailValidationPending_GetPMTAOnly",
                    commandType: CommandType.StoredProcedure);
                );

                // Using a semaphore to limit concurrent tasks
                SemaphoreSlim semaphore = new SemaphoreSlim(20);

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
                    Console.WriteLine(email.EmailAddress);
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
                //batch.Open();

                await batch.ExecuteAsync("EmailBatchValidationStart_Save", 
                    new { EmailBatch_ID = BatchID },
                    commandType: CommandType.StoredProcedure);

                IEnumerable<Emails> ValEmails = await batch.QueryAsync<Emails>("EmailValidation_GetByBatch",
                    new { EmailBatch_ID = BatchID },
                    commandType: CommandType.StoredProcedure);

                var tasks = ValEmails.Select(email => Task.Run(() => ValidateWithPowerMTA(email.EmailAddress))).ToList();

                await Task.WhenAll(tasks);

                await batch.ExecuteAsync("EmailBatchValidationFinished_Save", 
                    new { EmailBatch_ID = BatchID });

                //batch.Close();
            }
        }

        public static async Task<int> ValidateWithPowerMTA(string email)
        {

            var options = new RestClientOptions()
            {
                //Authenticator = new HttpBasicAuthenticator("api", EngineSetting.OutboundPP),
                BaseUrl = new Uri("https://api.sparkpost.com/api/v1/recipient-validation/single/" + Uri.EscapeDataString(email)),
                MaxTimeout = 1800000000
            };
            RestClient client = new RestClient(options);
            RestRequest request = new RestRequest()
            {
                Method = Method.Get
            };
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Authorization", "68b5274962da556a4ac7da138495d277b673bfa6");

            RestResponse response = client.Execute(request);
            ValidationRoot Validation = JsonConvert.DeserializeObject<ValidationRoot>(response.Content);
            string connectionString = ConfigurationManager.ConnectionStrings["DataCenterEmailEngine"].ConnectionString;

            using (IDbConnection batches = new SqlConnection(connectionString))
            {
                try
                {
                    await batches.ExecuteAsync("EmailValidation_SaveV2 ",
                        new
                        {
                            address = email.ToLower(),
                            isDisposableAddress = Validation.results.is_disposable,
                            isRoleAddress = Validation.results.is_role,
                            reason = Validation.results.reason ?? "",
                            result = Validation.results.result ?? "",
                            DeliveryConfidence = Validation.results.delivery_confidence,
                            did_you_mean = Validation.results.did_you_mean ?? ""
                        },
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 180);
                }
                catch (Exception e)
                {
                    Console.WriteLine("PowerMTA - " + e.Message + " - " + email.ToLower());
                }
            }
            return 1;
        }

        static async Task ValidateWithEASend(string email)
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

                        await EmailValidation.ExecuteAsync("EmailValidation_SaveV2 ",
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

        static async Task ValidateWithEASendPowerMTA(string email)
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
                    using (IDbConnection EmailValidation = new SqlConnection(connectionString))
                    {
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
                            },
                            commandTimeout: 180);
                    }
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

