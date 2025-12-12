using crmchapultepec.Components;
using crmchapultepec.Components.Account;
using crmchapultepec.data;
using crmchapultepec.data.Data;
using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.data.Repositories.Users;
using crmchapultepec.services.Hubs;
using crmchapultepec.services.Implementation.EvolutionWebhook;
using crmchapultepec.services.Implementation.UsersService;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using crmchapultepec.services.Interfaces.UsersService;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);


// HttpClient para EvolutionClient
//builder.Services.AddHttpClient<EvolutionClient>(client =>
//{
//    client.BaseAddress = new Uri(builder.Configuration["Evolution:BaseUrl"] ?? "https://your-evolution-host/");
//    client.Timeout = TimeSpan.FromSeconds(30);
//});
//builder.Services.AddSingleton<EvolutionClient>(); 
// opcional si el ctor no usa DI HttpClientFactory directamente
//builder.Services.AddHttpClient<EvolutionClient>();

// SignalR
builder.Services.AddSignalR();


builder.Services.AddControllers();      // <-- necesario para exponer los api controllers
// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<IUsersService, UsersService>();


builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, CustomClaimsPrincipalFactory>();

// Repositorio COMPARTIDO entre endpoint y componentes (YA USA SQL)
builder.Services.AddScoped<EvolutionWebhookRepository>();
builder.Services.AddScoped<IEvolutionWebhookService, EvolutionWebhookService>();


// Queue + Hosted Service
builder.Services.AddSingleton<InMemoryMessageQueue>();
builder.Services.AddSingleton<IMessageQueue>(sp => sp.GetRequiredService<InMemoryMessageQueue>());
builder.Services.AddHostedService<MessageProcessingService>();


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => {
    options.SignIn.RequireConfirmedAccount = true;
    // Fuerza email único y se utiliza en Edicion de usuarios del sistema (edit)
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
//.AddErrorDescriber<SpanishIdentityErrorDescriber>()   // Diccionario en español de errores de Identity
.AddDefaultTokenProviders();



builder.Services.AddDbContextFactory<ApplicationDbContext>(o =>
    o.UseSqlServer(connectionString));


// Fábrica para crear contextos EF por operación en el repositorio singleton
//builder.Services.AddDbContextFactory<CrmInboxDbContext>(options =>
//    options.UseSqlServer(connectionString));

builder.Services.AddDbContextFactory<CrmInboxDbContext>(options =>
    options.UseSqlServer(connectionString,
        sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();



builder.Services.AddRazorPages();
//builder.Services.AddRadzenComponents();
builder.Services.AddHttpContextAccessor();
// opcional: builder.Services.AddAntiforgery();  // usa valores por defecto

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.MapControllers();                   // <-- expone los ApiControllers
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapHub<CrmHub>("/crmHub");

app.MapRazorPages();



app.Run();