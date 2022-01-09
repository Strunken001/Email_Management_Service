using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Data.Framework.DataAccess.ProvidersInterface;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Email_Ticket_Fetcher
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private static CancellationTokenSource _done;
        private ImapClient _imap;
        private string connectionString;
        private readonly IUnitOfWork _unitOfWork;
        private IConfiguration _configuration;


        public Worker(ILogger<Worker> logger, ImapClient imap, IConfiguration configuration, IUnitOfWork unitOfWork)
        {
            _logger = logger;
            _imap = imap;
            _configuration = configuration;
            _unitOfWork = unitOfWork;
            connectionString = configuration.GetSection("ConnectionStrings").GetValue<string>("DefaultConnection");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await FetchUnreadEmails();
                await Task.Delay(1000, stoppingToken);
            }
        }

        public async Task FetchUnreadEmails()
        {
            _imap = new ImapClient();

            _imap.Connect(_configuration.GetValue<string>("SMTPIP"), Convert.ToInt32(_configuration.GetValue<string>("SMTPPort")), true);
            _imap.AuthenticationMechanisms.Remove("XOAUTH");
            _imap.Authenticate(_configuration.GetValue<string>("EmailAddress"), _configuration.GetValue<string>("Password"));

            _imap.Inbox.Open(FolderAccess.ReadWrite);
            _imap.Inbox.MessagesArrived += Inbox_MessagesArrived;
            _done = new CancellationTokenSource();
            _imap.Idle(_done.Token);
        }

        public void Inbox_MessagesArrived(object sender, EventArgs e)
        {
            _logger.LogInformation("New message received. Beginning push process");
            var folder = (ImapFolder)sender;
            //_done.Cancel(); // Stop idle process
            using (var client = new ImapClient())
            {
                client.Connect(_configuration.GetValue<string>("SMTPIP"), _configuration.GetValue<int>("SMTPPort"), true);
                _logger.LogInformation("Connected to IP and Port Successfully");
                // disable OAuth2 authentication unless you are actually using an access_token
                client.AuthenticationMechanisms.Remove("XOAUTH2");

                client.Authenticate(_configuration.GetValue<string>("EmailAddress"), _configuration.GetValue<string>("Password"));
                _logger.LogInformation("Email Authenticated successfully");
                int tmpcnt = 0;
                client.Inbox.Open(FolderAccess.ReadWrite);
                _logger.LogInformation("Inbox opened");
                foreach (var uid in client.Inbox.Search(SearchQuery.NotSeen))
                {
                    try
                    {
                        var message = client.Inbox.GetMessage(uid);
                        client.Inbox.SetFlags(uid, MessageFlags.Seen, true);

                        List<byte[]> listAttachment = new List<byte[]>();

                        if (message.Attachments.Count() > 0)
                        {
                            _logger.LogInformation("Message contains attachment(s)");
                            foreach (var objAttach in message.Attachments)
                            {
                                using (MemoryStream ms = new MemoryStream())
                                {
                                    ((MimeKit.ContentObject)(((MimeKit.MimePart)(objAttach)).ContentObject)).Stream.CopyTo(ms);
                                    byte[] objByte = ms.ToArray();
                                    listAttachment.Add(objByte);
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Message contains no attachments");
                        }

                        string subject = message.Subject;
                        string text = message.TextBody;
                        string to = message.To.ToString();
                        //string messageSender = message.Sender.ToString();
                        string from = message.From.ToString();
                        DateTimeOffset messageDate = message.Date;
                        insertNewEmail(subject, text, from, to, messageDate);
                        //var hubContext = GlobalHost.ConnectionManager.GetHubContext<IHub>();
                        //hubContext.Clients.All.modify("fromMail", text);
                        tmpcnt++;
                    }
                    catch (Exception mm)
                    {
                        _logger.LogError("An error has occured while reading email: " + mm.Message + mm.StackTrace);
                    }
                }
                client.Disconnect(true);
            }
        }

        public int insertNewEmail(string Subject, string message, string sender, string receiver, DateTimeOffset date)
        {
            int resp = 0;
            string agent = "Agent Aresejabata";
            var query = "insert into EmailTicket values('" + sender + "', '" + receiver + "', '" + Subject + "', '" + message + "', '" + agent + "', '" + date + "', '" +  0 +  "', '" + "NULL"  + "' , '" + "NULL" + "' , '" + "NULL" + "')";
            _logger.LogInformation("Proceeding to insert new email with sender: " + sender + ", receiver: " + receiver + ", for message date: " + date + " with subject: " + Subject);
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.CommandType = CommandType.Text;

                        conn.Open();
                        resp = cmd.ExecuteNonQuery();
                        conn.Close();
                        _logger.LogInformation("Response from insert: " + resp);

                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("An error has occured while sending new email to DB: " + ex.Message + ex.StackTrace);
                }

            }
            return resp;
        }

    }
}
