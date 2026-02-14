using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 1) Controllers
builder.Services.AddControllers();

// 2) Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3) DbContext (SQLite example)
builder.Services.AddDbContext<ExchangeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<INotificationProducer, KafkaNotificationProducer>();

var app = builder.Build();

var disableHttpsRedirection = builder.Configuration.GetValue<bool>("DisableHttpsRedirection");
var instanceName = builder.Configuration["InstanceName"] ?? Environment.GetEnvironmentVariable("INSTANCE_NAME") ?? "api";

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!disableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Instance-Name"] = instanceName;
    await next();
});

app.UseAuthorization();

// Ensure DB is created/migrated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExchangeDbContext>();
    db.Database.Migrate();
}

// 5) Map controllers
app.MapControllers();

app.Run();
