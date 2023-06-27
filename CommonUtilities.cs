using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Insight.Database;
using Microsoft.Data.SqlClient;
using System.Configuration;
using System.Data;

namespace Scoredlist.NET.Utilities
{
    public enum MailFormat
    {
        HTML,
        PlainText
    }
    public static class Extensions
    {
        public static NameValueCollection ToNameValueCollection<T>(this T dynamicObject)
        {
            var nameValueCollection = new NameValueCollection();
            foreach (PropertyDescriptor propertyDescriptor in TypeDescriptor.GetProperties(dynamicObject))
            {
                string value = propertyDescriptor.GetValue(dynamicObject) == null ? "" : propertyDescriptor.GetValue(dynamicObject).ToString();
                nameValueCollection.Add(propertyDescriptor.Name, value);
            }
            return nameValueCollection;
        }
    }

    public static class CommonUtilities
    {
        public static string StandardKey = "xT" + "ZJc" + "tH2" + "3TzJ" + "Tjk9" + "7jDU" + "RPQC" + "jXdW" + "awvj";
        public static string StandardIV = "HJ" + "kw" + "Kq" + "+Mo" + "NU=";

        public static string ParseXMLSecondaryNode(XmlNode NodeToParse, string XPath, string XPathSecondary)
        {
            if (NodeToParse.SelectSingleNode(XPath) != null) return NodeToParse.SelectSingleNode(XPath).InnerText.Trim();
            else if (NodeToParse.SelectSingleNode(XPathSecondary) != null) return NodeToParse.SelectSingleNode(XPathSecondary).InnerText.Trim();
            else return null;
        }
        public static string ParseXMLNode(XmlNode NodeToParse, string XPath)
        {
            if (NodeToParse.SelectSingleNode(XPath) != null) return NodeToParse.SelectSingleNode(XPath).InnerText.Trim();
            else return null;
        }

        public static string Capitalize(string value)
        {
            if (!String.IsNullOrEmpty(value))
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLower());
            else
                return "";
        }
        public static string Right(this string value, int length)
        {
            if (String.IsNullOrEmpty(value)) return "";
            if (length > value.Length) length = value.Length;
            return value.Substring(value.Length - length, length);
        }

        public static string StripTags(string source)
        {
            char[] array = new char[source.Length];
            int arrayIndex = 0;
            bool inside = false;

            for (int i = 0; i < source.Length; i++)
            {
                char let = source[i];
                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }
            return new string(array, 0, arrayIndex);
        }
        public static string Base59Encode(int value)
        {
            string returnValue = "";
            string Base59 = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789abcdefghijklmnpqrstuvwxyz";
            int workingValue = value;
            while (workingValue >= 59)
            {
                returnValue = returnValue + Base59.Substring(workingValue % 59, 1);
                workingValue = workingValue / 59;
            }
            returnValue = returnValue + Base59.Substring(workingValue, 1);
            return returnValue;
        }
        public static int Base59Decode(string value)
        {
            int returnValue = 0;
            string Base59 = "ABCDEFGHIJKLMNPQRSTUVWXYZ123456789abcdefghijklmnpqrstuvwxyz";
            string workingValue = Reverse(value);
            for (int i = 0; i < workingValue.Length; i++) returnValue = (returnValue * 59) + (Base59.IndexOf(workingValue.Substring(i, 1)));
            return returnValue;
        }
        public static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
        public static string StandardEncryptText(string Data)
        {
            try
            {
                byte[] CodecKey = Convert.FromBase64String(StandardKey);
                byte[] CodecIV = Convert.FromBase64String(StandardIV);

                using (MemoryStream mmStream = new MemoryStream())
                {
                    // Create a CryptoStream using the MemoryStream and the passed key and initialization vector (IV).
                    using (CryptoStream cStream = new CryptoStream(mmStream, new TripleDESCryptoServiceProvider().CreateEncryptor(CodecKey, CodecIV), CryptoStreamMode.Write))
                    {
                        byte[] toEncrypt = new ASCIIEncoding().GetBytes(Data);

                        // Write the byte array to the crypto stream and flush it.
                        cStream.Write(toEncrypt, 0, toEncrypt.Length);
                        cStream.FlushFinalBlock();

                        // Get an array of bytes from the MemoryStream that holds the encrypted data.
                        byte[] ret = mmStream.ToArray();
                        return Convert.ToBase64String(ret).Replace('+', '-').Replace('/', '_').TrimEnd('=');
                    }
                }
            }
            catch (CryptographicException e)
            {
                string TheMessage = e.Message;
                return null;
            }
        }
        public static string StandardDecryptText(string Data)
        {
            try
            {
                if (Data != null)
                {
                    if (Data != "0" && !int.TryParse(Data, out int intData))
                    {
                        byte[] DataDecode = Convert.FromBase64String(Data.Replace('-', '+').Replace('_', '/').PadRight(4 * ((Data.Length + 3) / 4), '='));
                        byte[] CodecKey = Convert.FromBase64String(StandardKey);
                        byte[] CodecIV = Convert.FromBase64String(StandardIV);

                        if (DataDecode != null)
                        {
                            // Create a new MemoryStream using the passed 
                            // array of encrypted data.
                            MemoryStream msDecrypt = new MemoryStream(DataDecode);

                            // Create a CryptoStream using the MemoryStream 
                            // and the passed key and initialization vector (IV).
                            CryptoStream csDecrypt = new CryptoStream(msDecrypt,
                                new TripleDESCryptoServiceProvider().CreateDecryptor(CodecKey, CodecIV),
                                CryptoStreamMode.Read);

                            // Create buffer to hold the decrypted data.
                            byte[] fromEncrypt = new byte[Data.Length];

                            // Read the decrypted data out of the crypto stream
                            // and place it into the temporary buffer.
                            csDecrypt.Read(fromEncrypt, 0, fromEncrypt.Length);

                            //Convert the buffer into a string and return it.
                            return new ASCIIEncoding().GetString(fromEncrypt).Replace("\0", "");
                        }
                        else return null;
                    }
                    else return Data;
                }
                else return null;
            }
            catch (CryptographicException e)
            {
                EventLog eventlog = new EventLog("Application")
                {
                    Source = "Syrinx.Utilities.CommonUtilities.StandardDecryptText"
                };
                eventlog.WriteEntry("Operation Failed " + e.Message, EventLogEntryType.Error);
                return null;
            }
        }

        public static string HTMLEncode(string Unencoded)
        {
            return System.Web.HttpUtility.HtmlEncode(Unencoded);
        }
        public static string HTMLDecode(string encoded)
        {
            return System.Web.HttpUtility.HtmlDecode(encoded);
        }
        public static string URLEncode(string Unencoded)
        {
            return System.Web.HttpUtility.UrlEncode(Unencoded);
        }
        public static string URLDecode(string encoded)
        {
            return System.Web.HttpUtility.UrlDecode(encoded);
        }

        public static string MD5Encrypt(string Unencrypted)
        {
            Encoder ByteEncoder = System.Text.Encoding.ASCII.GetEncoder();
            MD5 md5 = new MD5CryptoServiceProvider();

            byte[] UnencryptedByte = new byte[Unencrypted.Length];
            ByteEncoder.GetBytes(Unencrypted.ToString().ToCharArray(), 0, Unencrypted.Length, UnencryptedByte, 0, true);

            byte[] encryptedByte = md5.ComputeHash(UnencryptedByte);
            return BitConverter.ToString(encryptedByte).Replace("-", "").ToLower();
        }
        public static long ConvertIPToLong(string ipAddress)
        {

            if (System.Net.IPAddress.TryParse(ipAddress, out System.Net.IPAddress ip))
            {
                byte[] bytes = ip.GetAddressBytes();

                return (long)
                    (
                    16777216 * (long)bytes[0] +
                    65536 * (long)bytes[1] +
                    256 * (long)bytes[2] +
                    (long)bytes[3]
                    )
                    ;
            }
            else
                return 0;
        }
        public static Location GetLocation(string ipAddress)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["ScoredListConnectionString"].ConnectionString;
            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                return connection.QuerySql<Location>(
                    "EXEC IPGeolocation_Get @IPInteger",
                    new { IPInteger = ConvertIPToLong(ipAddress) }
                ).FirstOrDefault();
            }
        }

        public class Location
        {
            public string country { get; set; }
            public string region { get; set; }
            public string city { get; set; }
            public string postalCode { get; set; }
            public decimal latitude { get; set; }
            public decimal longitude { get; set; }
            public string metroCode { get; set; }
            public string areaCode { get; set; }
            public string Alpha3Code { get; set; }
        }

        public static bool SendMailMessage(string from, string to, string cc, string bcc, string subject, string body, MailFormat bodyFormat)
        {
            bool status = false;
            SmtpClient mSmtpClient = new SmtpClient();
            MailMessage mMailMessage = new MailMessage();

            System.Net.NetworkCredential basicCredential = new System.Net.NetworkCredential("noreply@tealeades.com", "N05p4mPl34s3");
            mSmtpClient.Host = "mail.tealeades.com";
            mSmtpClient.UseDefaultCredentials = false;
            mSmtpClient.Credentials = basicCredential;
            mSmtpClient.Port = 25;
            mSmtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

            string SeparatedTo = to.Replace(",", "||").Replace(";", "||");
            string SeparatedCC = cc.Replace(",", "||").Replace(";", "||");
            string SeparatedBCC = bcc.Replace(",", "||").Replace(";", "||");

            if (!String.IsNullOrEmpty(from) && !String.IsNullOrEmpty(SeparatedTo))
            {
                try
                {
                    mMailMessage.From = new MailAddress(from.Trim());
                    if (SeparatedTo.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedTo.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.To.Add(new MailAddress(Address.Trim()));
                    }
                    else mMailMessage.To.Add(new MailAddress(SeparatedTo.Trim()));

                    if (SeparatedCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.CC.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedCC.Trim())) mMailMessage.CC.Add(new MailAddress(SeparatedCC.Trim()));
                    }

                    if (SeparatedBCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedBCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.Bcc.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedBCC.Trim())) mMailMessage.Bcc.Add(new MailAddress(SeparatedBCC.Trim()));
                    }
                    // Set the subject of the mail message
                    mMailMessage.Subject = subject;
                    // Set the body of the mail message
                    mMailMessage.Body = body;
                    // Set the format of the mail message body as HTML
                    if (bodyFormat == MailFormat.PlainText) mMailMessage.IsBodyHtml = false;
                    if (bodyFormat == MailFormat.HTML) mMailMessage.IsBodyHtml = true;
                    // Set the priority of the mail message to normal
                    mMailMessage.Priority = MailPriority.Normal;
                    mMailMessage.BodyEncoding = System.Text.Encoding.UTF8;

                    //Send the mail message
                    //*******************************************************************************************************
                    mSmtpClient.Send(mMailMessage);
                    //*******************************************************************************************************
                    status = true;
                }
                catch (Exception e)
                {
                    var fromAddress = new MailAddress("DecisionlinksErrorLog@gmail.com", "");
                    var toAddress = new MailAddress("jodonnell@decisionlinks.com", "");
                    var toAddress2 = new MailAddress("ErrorMonitor@buddymurphy.com", "");
                    string fromPassword = "#  a_7 54 Dec1$1onlInk5 S3cur3";
                    string Esubject = "Scoredlist SendErrorMailMessage Error";
                    string Ebody = e.Message;

                    var smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        Port = 587,
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false,
                        Credentials = new System.Net.NetworkCredential(fromAddress.Address, fromPassword)
                    };
                    var message = new MailMessage(fromAddress, toAddress);
                    message.To.Add(toAddress2);
                    message.Subject = Esubject;
                    message.Body = Ebody;
                    {
                        smtp.Send(message);
                    }
                }

            }

            return status;
        }
        public static bool SendMailMessageAttachment(string from, string to, string cc, string bcc, string subject, string body, MailFormat bodyFormat, Attachment attachment)
        {
            bool status = false;
            SmtpClient mSmtpClient = new SmtpClient();
            MailMessage mMailMessage = new MailMessage();

            System.Net.NetworkCredential basicCredential = new System.Net.NetworkCredential("noreply@tealeades.com", "N05p4mPl34s3");
            mSmtpClient.Host = "mail.tealeades.com";
            mSmtpClient.UseDefaultCredentials = false;
            mSmtpClient.Credentials = basicCredential;
            mSmtpClient.Port = 25;
            mSmtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

            string SeparatedTo = to.Replace(",", "||").Replace(";", "||");
            string SeparatedCC = cc.Replace(",", "||").Replace(";", "||");
            string SeparatedBCC = bcc.Replace(",", "||").Replace(";", "||");

            if (!String.IsNullOrEmpty(from) && !String.IsNullOrEmpty(SeparatedTo))
            {
                try
                {
                    mMailMessage.From = new MailAddress(from.Trim());
                    if (SeparatedTo.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedTo.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.To.Add(new MailAddress(Address.Trim()));
                    }
                    else mMailMessage.To.Add(new MailAddress(SeparatedTo.Trim()));

                    if (SeparatedCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.CC.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedCC.Trim())) mMailMessage.CC.Add(new MailAddress(SeparatedCC.Trim()));
                    }

                    if (SeparatedBCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedBCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.Bcc.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedBCC.Trim())) mMailMessage.Bcc.Add(new MailAddress(SeparatedBCC.Trim()));
                    }
                    // Set the subject of the mail message
                    mMailMessage.Subject = subject;
                    // Set the body of the mail message
                    mMailMessage.Body = body;
                    // Set the format of the mail message body as HTML
                    if (bodyFormat == MailFormat.PlainText) mMailMessage.IsBodyHtml = false;
                    if (bodyFormat == MailFormat.HTML) mMailMessage.IsBodyHtml = true;
                    // Set the priority of the mail message to normal
                    mMailMessage.Priority = MailPriority.Normal;
                    mMailMessage.BodyEncoding = System.Text.Encoding.UTF8;

                    // Add attachment to the mail message
                    mMailMessage.Attachments.Add(attachment);

                    //Send the mail message
                    //*******************************************************************************************************
                    mSmtpClient.Send(mMailMessage);
                    //*******************************************************************************************************
                    status = true;
                }
                catch (Exception e)
                {
                    var fromAddress = new MailAddress("DecisionlinksErrorLog@gmail.com", "");
                    var toAddress = new MailAddress("jodonnell@decisionlinks.com", "");
                    var toAddress2 = new MailAddress("ErrorMonitor@buddymurphy.com", "");
                    string fromPassword = "#  a_7 54 Dec1$1onlInk5 S3cur3";
                    string Esubject = "Scoredlist SendErrorMailMessage Error";
                    string Ebody = e.Message;

                    var smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        Port = 587,
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false,
                        Credentials = new System.Net.NetworkCredential(fromAddress.Address, fromPassword)
                    };
                    var message = new MailMessage(fromAddress, toAddress);
                    message.To.Add(toAddress2);
                    message.Subject = Esubject;
                    message.Body = Ebody;
                    {
                        smtp.Send(message);
                    }
                }
            }
            return status;
        }
        public static bool SendErrorMailMessage(string from, string to, string cc, string bcc, string subject, string body, MailFormat bodyFormat)
        {
            bool status = false;
            SmtpClient mSmtpClient = new SmtpClient();
            MailMessage mMailMessage = new MailMessage();

            System.Net.NetworkCredential basicCredential = new System.Net.NetworkCredential("noreply@tealeades.com", "N05p4mPl34s3");
            mSmtpClient.Host = "mail.tealeades.com";
            mSmtpClient.UseDefaultCredentials = false;
            mSmtpClient.Credentials = basicCredential;
            mSmtpClient.Port = 25;
            mSmtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;

            string SeparatedTo = to.Replace(",", "||").Replace(";", "||");
            string SeparatedCC = cc.Replace(",", "||").Replace(";", "||");
            string SeparatedBCC = bcc.Replace(",", "||").Replace(";", "||");

            if (!String.IsNullOrEmpty(from) && !String.IsNullOrEmpty(SeparatedTo))
            {
                try
                {
                    // Set the sender address of the mail message
                    mMailMessage.From = new MailAddress(from.Trim());
                    // Set the recepient address of the mail message

                    if (SeparatedTo.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedTo.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.To.Add(new MailAddress(Address.Trim()));
                    }
                    else mMailMessage.To.Add(new MailAddress(SeparatedTo.Trim()));

                    if (SeparatedCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.CC.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedCC.Trim())) mMailMessage.CC.Add(new MailAddress(SeparatedCC.Trim()));
                    }

                    if (SeparatedBCC.IndexOf("||") > 0)
                    {
                        string[] allAddresses = SeparatedBCC.Split((new string[] { "||" }), StringSplitOptions.RemoveEmptyEntries);
                        foreach (string Address in allAddresses) if (!String.IsNullOrEmpty(Address.Trim())) mMailMessage.Bcc.Add(new MailAddress(Address.Trim()));
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(SeparatedBCC.Trim())) mMailMessage.Bcc.Add(new MailAddress(SeparatedBCC.Trim()));
                    }
                    // Set the subject of the mail message
                    mMailMessage.Subject = subject;
                    // Set the body of the mail message
                    mMailMessage.Body = body;
                    // Set the format of the mail message body as HTML
                    if (bodyFormat == MailFormat.PlainText) mMailMessage.IsBodyHtml = false;
                    if (bodyFormat == MailFormat.HTML) mMailMessage.IsBodyHtml = true;
                    // Set the priority of the mail message to normal
                    mMailMessage.Priority = MailPriority.Normal;
                    mMailMessage.BodyEncoding = System.Text.Encoding.UTF8;

                    //Send the mail message
                    //*******************************************************************************************************
                    mSmtpClient.Send(mMailMessage);
                    //*******************************************************************************************************
                    status = true;
                }
                catch (Exception e)
                {
                    var fromAddress = new MailAddress("DecisionlinksErrorLog@gmail.com", "");
                    var toAddress = new MailAddress("jodonnell@decisionlinks.com", "");
                    var toAddress2 = new MailAddress("ErrorMonitor@buddymurphy.com", "");
                    string fromPassword = "#  a_7 54 Dec1$1onlInk5 S3cur3";
                    string Esubject = "Scoredlist SendErrorMailMessage Error";
                    string Ebody = e.Message;

                    var smtp = new SmtpClient
                    {
                        Host = "smtp.gmail.com",
                        Port = 587,
                        EnableSsl = true,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false,
                        Credentials = new System.Net.NetworkCredential(fromAddress.Address, fromPassword)
                    };
                    var message = new MailMessage(fromAddress, toAddress);
                    message.To.Add(toAddress2);
                    message.Subject = Esubject;
                    message.Body = Ebody;
                    {
                        smtp.Send(message);
                    }
                }
            }
            return status;
        }
    }
}
