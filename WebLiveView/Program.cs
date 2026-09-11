using WebLiveView.Components;
using WebLiveView.Services;

// 创建 Blazor Server Web 应用构建器，配置扫码枪实时图像查看服务
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<ScannerLiveViewService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapGet("/mjpeg", async (HttpContext context, ScannerLiveViewService scannerService, IConfiguration configuration) =>
{
    // REVIEW-FIX: /mjpeg 端点增加 token 鉴权（?token=xxx），防止未授权访问实时图像流
    var token = configuration["MjpegSettings:Token"];
    if (string.IsNullOrWhiteSpace(token))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync("MjpegSettings:Token 未配置，无法提供视频流");
        return;
    }

    var providedToken = context.Request.Query["token"].ToString();
    if (!string.Equals(providedToken, token, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await scannerService.WriteMjpegStreamAsync(context.Response, context.RequestAborted);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
