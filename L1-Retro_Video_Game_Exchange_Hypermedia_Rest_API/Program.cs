using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1) Controllers
builder.Services.AddControllers();

// 2) Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3) DbContext (SQLite example)
builder.Services.AddDbContext<ExchangeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// 4) Swagger middleware (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();          // exposes /swagger/{doc}/swagger.json
    app.UseSwaggerUI();        // exposes /swagger and /swagger/index.html
}

app.UseHttpsRedirection();

app.UseAuthorization();

// 5) Map controllers
app.MapControllers();

app.Run();
