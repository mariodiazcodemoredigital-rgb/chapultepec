using crmchapultepec.Components;
using crmchapultepec.Components.Account;
using crmchapultepec.data;
using crmchapultepec.data.Data;
using crmchapultepec.data.Repositories.CRM;
using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.data.Repositories.Users;
using crmchapultepec.services.Hubs;
using crmchapultepec.services.Implementation.CRM;
using crmchapultepec.services.Implementation.EvolutionWebhook;
using crmchapultepec.services.Implementation.UsersService;
using crmchapultepec.services.Interfaces.CRM;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using crmchapultepec.services.Interfaces.UsersService;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Base de datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlServer(connectionString));

// Configuración robusta para el contexto del CRM (Reintentos activados)
builder.Services.AddDbContextFactory<CrmInboxDbContext>(options =>
    options.UseSqlServer(connectionString,
        sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

// 2. Identity
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// Ojo: Aquí restauré tu CustomClaimsPrincipalFactory si la usas
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, CustomClaimsPrincipalFactory>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => {
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// 3. Servicios Web (Blazor, API, SignalR)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// 4. TUS SERVICIOS DE NEGOCIO (Restaurados)

// --- Usuarios ---
builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<IUsersService, UsersService>();

// --- Evolution Webhook ---
builder.Services.AddScoped<EvolutionWebhookRepository>();
builder.Services.AddScoped<IEvolutionWebhookService, EvolutionWebhookService>();
builder.Services.AddScoped<WebhookControlRepository>();
builder.Services.AddScoped<IWebhookControlService, WebhookControlService>();
builder.Services.AddScoped<CrmMessageMediaRepository>();
builder.Services.AddScoped<ICrmMessageMediaService, CrmMessageMediaService>();

// --- CRM Admin ---
builder.Services.AddScoped<CRMxEquiposRepository>();
builder.Services.AddScoped<CRMxEquiposService>();
builder.Services.AddScoped<CRMxUsuariosRepository>();
builder.Services.AddScoped<CRMxUsuariosService>();
builder.Services.AddScoped<CRMxEquiposUsuariosRepository>();
builder.Services.AddScoped<CRMxEquiposUsuariosService>();

// --- CRM Inbox ---
builder.Services.AddHttpClient<CrmInboxRepository>();
//builder.Services.AddScoped<CrmInboxRepository>();
builder.Services.AddScoped<ICRMInboxService, CRMInboxService>();
builder.Services.AddScoped<CRMInboxService>(); // IMPORTANTE: Registro concreto para las vistas

// --- Background Services ---
builder.Services.AddSingleton<InMemoryMessageQueue>();
builder.Services.AddSingleton<IMessageQueue>(sp => sp.GetRequiredService<InMemoryMessageQueue>());
builder.Services.AddHostedService<MessageProcessingService>();


// 5. Otros
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 6. Pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Antiforgery SIEMPRE antes de los mapas
app.UseAntiforgery();

app.MapControllers(); // API Webhooks
app.MapHub<CrmHub>("/crmHub"); // SignalR
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(); // Blazor

app.MapAdditionalIdentityEndpoints();
app.MapRazorPages();

app.Run();