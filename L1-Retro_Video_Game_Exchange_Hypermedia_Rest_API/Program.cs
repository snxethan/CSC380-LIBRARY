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

// 3) DbContext (SQLite)
builder.Services.AddDbContext<ExchangeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 4) Kafka notification producer (singleton)
//    KafkaNotificationProducer wraps a Confluent.Kafka IProducer<Null, string>.
//    It is registered as a singleton so that the underlying TCP connection to
//    the Kafka broker (kafka:9092) is created once and reused for all requests,
//    rather than creating a new producer per HTTP request.
//
//    Connection settings come from appsettings.json / environment variables:
//      Kafka__BootstrapServers  — broker address (e.g. kafka:9092)
//      Kafka__Topic             — topic name     (e.g. notifications)
//
//    INotificationProducer is the interface; controllers depend on the
//    interface, not the concrete class, which makes unit testing easier.
builder.Services.AddSingleton<INotificationProducer, KafkaNotificationProducer>();

var app = builder.Build();

var disableHttpsRedirection = builder.Configuration.GetValue<bool>("DisableHttpsRedirection");
var instanceName = builder.Configuration["InstanceName"]
    ?? Environment.GetEnvironmentVariable("INSTANCE_NAME")
    ?? "api";

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!disableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

// Adds X-Instance-Name header to every response so nginx load-balancing
// can be verified (each API container has a distinct INSTANCE_NAME).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Instance-Name"] = instanceName;
    await next();
});

app.UseAuthorization();

// Ensure the SQLite database schema is up-to-date at startup.
// EF Core applies any pending migrations automatically so the database
// does not need to be manually seeded when running in Docker.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExchangeDbContext>();
    db.Database.Migrate();
}

// 5) Map controllers
app.MapControllers();

app.Run();

