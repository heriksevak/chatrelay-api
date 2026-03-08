using Microsoft.EntityFrameworkCore;
using ChatRelay.API.Models;

namespace ChatRelay.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<ApiKey> ApiKeys { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<IncomingMessage> IncomingMessages { get; set; }
        public DbSet<WhatsAppAccount> WhatsAppAccounts { get; set; }
        public DbSet<Template> Templates { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Campaign> Campaigns { get; set; }
        public DbSet<CampaignContact> CampaignContacts { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<GroupContact> GroupContacts { get; set; }
        public DbSet<WebhookLog> WebhookLog { get; set; }
    }

}