//using EASendMail;
//using Newtonsoft.Json;
//using RestSharp;
//using RestSharp.Authenticators;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//
//namespace EmailEngineValidator
//{
//    class Program
//    {
//        static void Main(string[] args)
//        {
//            int ValidationSource_ID = 0;//0=Today'sEmails ,1=EmailValidation1,2=Batch PowerMTAOnly, 3=Realtime Emails,10=EmailValidation1 PowerMTAOnly
//            DateTime DropDate = DateTime.Now;
//            int BatchID = 0;
//            if (args.Length > 0) int.TryParse(args[0], out ValidationSource_ID);
//            if (args.Length > 1 && (ValidationSource_ID == 0 || ValidationSource_ID == 3 || ValidationSource_ID == 4)) DateTime.TryParse(args[1], out DropDate);
//            if (args.Length > 1 && (ValidationSource_ID == 2)) int.TryParse(args[1], out BatchID);
//
//            switch (ValidationSource_ID)
//            {
//                case 0:
//                    ValidateBatches(DropDate, false);
//                    break;
//
//                case 1:
//                    ValidatePending();
//                    break;
//
//                case 2:
//                    ValidateBatchesPowerMTAOnly(BatchID);
//                    break;
//
//                case 3:
//                    ValidateBatches(DropDate, true);
//                    break;
//
//                case 4:
//                    ValidateWeek(DropDate);
//                    break;
//
//                case 10:
//                    ValidatePendingPowerMTAOnly();
//                    break;
//
//                default:
//                    ValidateBatches(DropDate, false);
//                    break;
//            }
//            if (!(new[] { 2, 3 }.Contains(ValidationSource_ID)))
//            {
//                using (PetaPoco.Database cleanup = new PetaPoco.Database("DataCenterEmailEngine"))
//                {
//                    cleanup.CommandTimeout = 9000;
//                    cleanup.Execute(";EXEC EmailValidationCleanUp_GetV2;");
//                }
//            }
//        }
//          done
//        public static void ValidateWeek(DateTime DropDate)
//        {
//            using (PetaPoco.Database batches = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                IEnumerable<WeeklyBatchModel> ValBatches = batches.Query<WeeklyBatchModel>(
//                    PetaPoco.Sql.Builder.Append(";EXEC EmailValidationNextWeek_GetV2")
//                    .Append("@@Date = @0", DropDate)
//                    );
//
//                //";EXEC EmailValidationNextWeek_GetV2");
//                foreach (WeeklyBatchModel email in ValBatches)
//                {
//                    Console.WriteLine("Batch: " + email.EmailBatch_ID + " Date: " + email.EmailDropDate.ToShortDateString());
//                    ValidateBatchesPowerMTAOnly(email.EmailBatch_ID);
//                };
//            }
//        }
//done
//        public static void ValidatePending()
//        {
//            using (PetaPoco.Database batches = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                List<Emails> ValEmails = new List<Emails>();
//                ValEmails = batches.Fetch<Emails>(";EXEC EmailValidationPending_GetV2;");
//                Parallel.ForEach(ValEmails, email =>
//                {
//                    if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
//                    {
//                        ValidateWithEASendPowerMTA(email.EmailAddress);
//                    }
//                    if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
//                    {
//                        ValidateWithPowerMTA(email.EmailAddress);
//                    }
//                });
//                ValEmails = batches.Fetch<Emails>(";EXEC EmailValidationPending_GetV2;");
//                foreach (Emails email in ValEmails)
//                {
//                    if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
//                    {
//                        ValidateWithEASendPowerMTA(email.EmailAddress);
//                    }
//                };
//            }
//        }
//          done
//        public static void ValidateBatches(DateTime DropDate, bool Realtime)
//        {
//            using (PetaPoco.Database batches = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                batches.CommandTimeout = 9000;
//                using (PetaPoco.Database EmailBatches = new PetaPoco.Database("DataCenterEmailEngine"))
//                {
//                    IEnumerable<int> Batches = EmailBatches.Query<int>(PetaPoco.Sql.Builder
//                                                                                .Append(";EXEC [dbo].[EmailBatches_GetForValidation]")
//                                                                                .Append("@@DropDate = @0", DropDate)
//                                                                                .Append(",@@Realtime = @0", Realtime));
//                    foreach (int batch in Batches)
//                    {
//                        batches.Execute(PetaPoco.Sql.Builder.Append(";EXEC EmailBatchValidationStart_Save").Append("@@EmailBatch_ID = @0", batch));
//
//                        List<Emails> ValEmails = batches.Fetch<Emails>(";EXEC EmailValidation_GetByBatch @0 ;", batch);
//                        Parallel.ForEach(ValEmails, email =>
//                        {
//                            if (new[] { 0, 7 }.Contains(email.EmailServiceProvider_ID))
//                            {
//                                ValidateWithEASendPowerMTA(email.EmailAddress);
//                            }
//                            if (new[] { 2 }.Contains(email.EmailServiceProvider_ID))
//                            {
//                                ValidateWithPowerMTA(email.EmailAddress);
//                            }
//                        });
//                        foreach (Emails email in ValEmails)
//                        {
//                            if (new[] { 1, 3, 4, 5, 8, 9 }.Contains(email.EmailServiceProvider_ID))
//                            {
//                                ValidateWithEASendPowerMTA(email.EmailAddress);
//                            }
//                        };
//                        //ValEmails = batches.Fetch<Emails>(";EXEC EmailValidation_GetByBatch @0 ;", batch);
//                        //Parallel.ForEach(ValEmails, new ParallelOptions { MaxDegreeOfParallelism = 1000 }, email =>
//                        //{
//                        //        ValidateWithPowerMTA(email.EmailAddress);
//                        //});
//                        batches.Execute(PetaPoco.Sql.Builder.Append(";EXEC EmailBatchValidationFinished_Save").Append("@@EmailBatch_ID = @0", batch));
//                    }
//                }
//            }
//        }
//done
//        public static void ValidatePendingPowerMTAOnly()
//        {
//            using (PetaPoco.Database batches = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                batches.CommandTimeout = 9000;
//                using (PetaPoco.Database EmailBatches = new PetaPoco.Database("DataCenterEmailEngine"))
//                {
//                    List<Emails> ValEmails = batches.Fetch<Emails>(";EXEC EmailValidationPending_GetPMTAOnly;");
//                    Parallel.ForEach(ValEmails, new ParallelOptions { MaxDegreeOfParallelism = 400 }, email =>
//                    {
//                        ValidateWithPowerMTA(email.EmailAddress);
//                    });
//                }
//            }
//        }
//         
//          done
//        public static void ValidateBatchesPowerMTAOnly(int BatchID)
//        {
//            using (PetaPoco.Database batches = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                batches.CommandTimeout = 9000;
//                batches.Execute(PetaPoco.Sql.Builder.Append(";EXEC EmailBatchValidationStart_Save").Append("@@EmailBatch_ID = @0", BatchID));
//
//                using (PetaPoco.Database EmailBatches = new PetaPoco.Database("DataCenterEmailEngine"))
//                {
//                    List<Emails> ValEmails = batches.Fetch<Emails>(";EXEC EmailValidation_GetByBatch @0 ;", BatchID);
//                    Parallel.ForEach(ValEmails, new ParallelOptions { MaxDegreeOfParallelism = 1000 }, email =>
//                    {
//                        ValidateWithPowerMTA(email.EmailAddress);
//                    });
//                }
//                batches.Execute(PetaPoco.Sql.Builder.Append(";EXEC EmailBatchValidationFinished_Save").Append("@@EmailBatch_ID = @0", BatchID));
//            }
//        }
//
//        public class Emails
//        {
//            public int EmailServiceProvider_ID { get; set; }
//            public string EmailAddress { get; set; }
//        }
//done
//        static int ValidateWithPowerMTA(string email)
//        {
//            var client = new RestClient("https://api.sparkpost.com/api/v1/recipient-validation/single/" + email);
//            var request = new RestRequest(Method.GET);
//            client.Timeout = -1;
//            request.AddHeader("Accept", "application/json");
//            request.AddHeader("Authorization", "68b5274962da556a4ac7da138495d277b673bfa6");
//
//            IRestResponse response = client.Execute(request);
//            ValidationRoot Validation = JsonConvert.DeserializeObject<ValidationRoot>(response.Content);
//            using (PetaPoco.Database EmailValidation = new PetaPoco.Database("DataCenterEmailEngine"))
//            {
//                try
//                {
//                    EmailValidation.CommandTimeout = 180;
//                    EmailValidation.Execute(PetaPoco.Sql.Builder
//                        .Append(";EXEC EmailValidation_SaveV2 ")
//                        .Append("@@address = @0", email.ToLower())
//                        .Append(", @@isDisposableAddress = @0", Validation.results.is_disposable)
//                        .Append(", @@isRoleAddress = @0", Validation.results.is_role)
//                        .Append(", @@reason = @0", Validation.results.reason ?? "")
//                        .Append(", @@result = @0", Validation.results.result ?? "")
//                        .Append(", @@DeliveryConfidence = @0", Validation.results.delivery_confidence)
//                        .Append(", @@did_you_mean = @0", Validation.results.did_you_mean ?? "")
//                    );
//                }
//                catch (Exception e)
//                {
//                    Console.WriteLine("PowerMTA - " + e.Message + " - " + email.ToLower());
//                }
//            }
//            return 1;
//        }
//
//        static string ValidateWithMailgun(string email)
//        {
//            RestClient client = new RestClient();
//            client.Authenticator = new HttpBasicAuthenticator("api", "dec256ffd0a59edf3233d6336356995a-9ce9335e-81a9150d");
//            RestRequest request = new RestRequest();
//            string result = "";
//            client.BaseUrl = new Uri("https://api.mailgun.net/v4");
//            request.Resource = "/address/validate";
//            request.AddParameter("address", email.ToLower());
//            IRestResponse respVal = client.Execute(request);
//            if (respVal.StatusCode.ToString() == "OK")
//            {
//                string resultsVal = respVal.Content.ToString();
//                dynamic ResultsValOB = JsonConvert.DeserializeObject(resultsVal);
//                if (ResultsValOB.reason.Type.ToString() == "Array") ResultsValOB.reason = String.Join(", ", ResultsValOB.reason);
//                resultsVal = resultsVal.Replace("[]", "\"\"");
//                result = ResultsValOB.result.ToString();
//                if (ResultsValOB.reason.ToString() == "mailbox_is_role_address") result = "undeliverable";
//                if (ResultsValOB.reason.ToString().Length > 3 && ResultsValOB.reason.ToString().Substring(0, 4).ToLower() == "smtp") result = "undeliverable";
//                using (PetaPoco.Database EmailValidation = new PetaPoco.Database("DataCenterEmailEngine"))
//                {
//                    EmailValidation.CommandTimeout = 180;
//                    EmailValidation.Execute(PetaPoco.Sql.Builder
//                        .Append(";EXEC EmailValidation_Save ")
//                        .Append("@@address = @0", ResultsValOB.address.ToString())
//                        .Append(", @@isDisposableAddress = @0", ResultsValOB.is_disposable_address.ToString())
//                        .Append(", @@isRoleAddress = @0", ResultsValOB.is_role_address.ToString())
//                        .Append(", @@reason = @0", ResultsValOB.reason.ToString())
//                        .Append(", @@result = @0", ResultsValOB.result.ToString())
//                        .Append(", @@risk = @0", ResultsValOB.risk.ToString())
//                    );
//                }
//            }
//            else
//            {
//                Console.WriteLine("Mailgun - " + respVal.ErrorMessage + " - " + respVal.ResponseStatus);
//            }
//            return result;
//        }
//
//        static void ValidateWithEASend(string email)
//        {
//            try
//            {
//                SmtpServer vServer = new SmtpServer("");
//                SmtpClient oSmtp = new SmtpClient();
//                SmtpMail oMail = new SmtpMail("ES-E1582190613-00899-DU956331B9EA29VA-51T11E9DD8A7D591");
//                oMail.From = "support@tealeades.com";
//                oMail.To = email.ToLower();
//                oSmtp.TestRecipients(vServer, oMail);
//            }
//            catch (Exception vep)
//            {
//                if (!vep.Message.ToLower().Contains("too many") &&
//                    !vep.Message.ToLower().Contains("blocked") &&
//                    !vep.Message.ToLower().Contains("delayed") &&
//                    !vep.Message.ToLower().Contains("refused") &&
//                    !vep.Message.ToLower().Contains("exceeded") &&
//                    !vep.Message.ToLower().Contains("try again") &&
//                    !vep.Message.Contains("74.118.137.7"))
//                {
//                    using (PetaPoco.Database EmailValidation = new PetaPoco.Database("DataCenterEmailEngine"))
//                    {
//                        EmailValidation.CommandTimeout = 180;
//                        EmailValidation.Execute(PetaPoco.Sql.Builder
//                           .Append(";EXEC EmailValidation_SaveV2 ")
//                           .Append("@@address = @0", email.ToLower())
//                           .Append(", @@isDisposableAddress = @0", "False")
//                           .Append(", @@isRoleAddress = @0", "False")
//                           .Append(", @@reason = @0", vep.Message ?? "")
//                           .Append(", @@result = @0", "undeliverable")
//                           .Append(", @@DeliveryConfidence = @0", 0)
//                           .Append(", @@did_you_mean = @0", "")
//                       );
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("EASend - " + vep.Message + " - " + email.ToLower());
//                }
//            }
//        }

//done
//        static void ValidateWithEASendPowerMTA(string email)
//        {
//            try
//            {
//                SmtpServer vServer = new SmtpServer("");
//                SmtpClient oSmtp = new SmtpClient();
//                SmtpMail oMail = new SmtpMail("ES-E1582190613-00899-DU956331B9EA29VA-51T11E9DD8A7D591");
//                oMail.From = "support@tealeades.com";
//                oMail.To = email.ToLower();
//                oSmtp.TestRecipients(vServer, oMail);
//                ValidateWithPowerMTA(email);
//            }
//            catch (Exception vep)
//            {
//                if (!vep.Message.ToLower().Contains("too many") &&
//                    !vep.Message.ToLower().Contains("blocked") &&
//                    !vep.Message.ToLower().Contains("delayed") &&
//                    !vep.Message.ToLower().Contains("refused") &&
//                    !vep.Message.ToLower().Contains("exceeded") &&
//                    !vep.Message.ToLower().Contains("try again") &&
//                    !vep.Message.Contains("74.118.137.7"))
//                {
//                    using (PetaPoco.Database EmailValidation = new PetaPoco.Database("DataCenterEmailEngine"))
//                    {
//                        EmailValidation.CommandTimeout = 180;
//                        EmailValidation.Execute(PetaPoco.Sql.Builder
//                           .Append(";EXEC EmailValidation_SaveV2 ")
//                           .Append("@@address = @0", email.ToLower())
//                           .Append(", @@isDisposableAddress = @0", "False")
//                           .Append(", @@isRoleAddress = @0", "False")
//                           .Append(", @@reason = @0", vep.Message ?? "")
//                           .Append(", @@result = @0", "undeliverable")
//                           .Append(", @@DeliveryConfidence = @0", 0)
//                           .Append(", @@did_you_mean = @0", "")
//                       );
//                    }
//                }
//                else
//                {
//                    ValidateWithPowerMTA(email);
//                    Console.WriteLine("EASend - " + vep.Message + " - " + email.ToLower());
//                }
//            }
//        }
//
//        // ValidationRoot myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
//        public class Results
//        {
//            public bool valid { get; set; }
//            public string result { get; set; }
//            public string reason { get; set; }
//            public bool is_role { get; set; }
//            public bool is_disposable { get; set; }
//            public bool is_free { get; set; }
//            public int delivery_confidence { get; set; }
//            public string did_you_mean { get; set; }
//        }
//
//        public class ValidationRoot
//        {
//            public Results results { get; set; }
//        }
//
//        public class BatchModel
//        {
//            public int EmailBatch_ID { get; set; }
//            public string BusinessName { get; set; }
//            public string FromEmailAddress { get; set; }
//            public string ReplyToEmailAddress { get; set; }
//            public string SubjectLine { get; set; }
//            public string TemplateURL { get; set; }
//            public string Unsubscribe { get; set; }
//            public int FailedEmailCount { get; set; }
//            public bool ClickTracking { get; set; }
//        }
//        public class WeeklyBatchModel
//        {
//            public int EmailBatch_ID { get; set; }
//            public DateTime EmailDropDate { get; set; }
//        }
//    }
//}
////List<string> ValEmails = new List<string>();
////switch (ValidationSource_ID)
////{
////    case 0:
////        ValEmails = batches.Fetch<string>(";EXEC EmailEngineValidationTodaysBatches_Get;");
////        break;
//
////    case 1:
////        ValEmails = batches.Fetch<string>(";EXEC EmailValidationPending_Get;");
//        break;

//    case 2:
//        ValEmails = batches.Fetch<string>(";EXEC EmailValidationPending2_Get;");
//        break;

//    default:
//        ValEmails = batches.Fetch<string>(";EXEC EmailEngineValidationTodaysBatches_Get;");
//        break;
//}
