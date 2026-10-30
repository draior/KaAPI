using KaApi.Models;
using KaApi.Services;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
var configuredUrls = builder.Configuration["Server:Urls"];
var serviceName = builder.Configuration["WindowsService:ServiceName"] ?? "KaApi";
var databaseConnectionString = builder.Configuration.GetConnectionString("KmDatabase")
    ?? throw new InvalidOperationException("Connection string 'KmDatabase' is missing.");
var databaseConnection = new SqlConnectionStringBuilder(databaseConnectionString);
var fileStorageOptions = builder.Configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>()
    ?? new FileStorageOptions();
var runtimeFilePaths = new RuntimeFilePaths(fileStorageOptions);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = serviceName;
});

if (!string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls(configuredUrls);
}

// Keep the file trace complete while limiting the service console to sale headers and errors.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileTraceLoggerProvider(runtimeFilePaths));
builder.Logging.AddProvider(new ConsoleMasterDataLoggerProvider());
builder.Services.AddControllers()
    .AddXmlSerializerFormatters();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton(runtimeFilePaths);
builder.Services.AddSingleton<IPayloadArchiveService, PayloadArchiveService>();
builder.Services.AddScoped<KaApi.Services.ISalesService, KaApi.Services.SalesService>();
builder.Services.AddScoped<KaApi.Services.IOrderXmlImportService, KaApi.Services.OrderXmlImportService>();
builder.Services.AddHostedService<KaApi.Services.StartupOrderActivationService>();
builder.Services.AddHostedService<KaApi.Services.DatabaseStartupCheckService>();
builder.Services.Configure<KaApi.Models.OrderXmlImportDefaults>(
    builder.Configuration.GetSection("OrderXmlImportDefaults"));
builder.Services.Configure<FileStorageOptions>(
    builder.Configuration.GetSection(FileStorageOptions.SectionName));
builder.Services.Configure<StartupOrderActivationOptions>(
    builder.Configuration.GetSection(StartupOrderActivationOptions.SectionName));

var app = builder.Build();

app.Logger.LogInformation(
    "KaApi startup file locations. TraceFile={TraceFile}, PayloadRoot={PayloadRoot}",
    runtimeFilePaths.TraceFilePath,
    runtimeFilePaths.PayloadRootPath);
app.Logger.LogInformation(
    "KaApi startup configuration. ServiceName={ServiceName}, ApiUrls={ApiUrls}, DatabaseServer={DatabaseServer}, DatabaseName={DatabaseName}",
    serviceName,
    configuredUrls ?? "Not configured",
    databaseConnection.DataSource,
    databaseConnection.InitialCatalog);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.UseMiddleware<RequestResponseArchiveMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
