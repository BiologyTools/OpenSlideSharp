﻿using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace SingleSlideServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ImageOption>(Configuration.GetSection("Image"));

            services.AddSingleton<ImageProvider>();

            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.Map("/image_files", appBuilder =>
            {
                appBuilder.Use(async (context, next) =>
                {
                    if (!TryParseDeepZoom(context.Request.Path, out var result))
                    {
                        await next();
                        return;
                    }

                    if (result.format != "jpeg")
                    {
                        await next();
                        return;
                    }

                    var provider = context.RequestServices.GetService<ImageProvider>();
                    var response = context.Response;
                    response.ContentType = "image/jpeg";
                    response.Body = await Task.Run(()=> new MemoryStream(provider.DeepZoomGenerator.GetTile(result.level, result.col, result.row,out var tmp)));
                });
            });

            app.UseDefaultFiles();

            app.UseStaticFiles();

            app.UseMvc();
        }

        // OPTIMIZE: Use ReadOnlySpan in .NET Core 2.1
        // expression: /{level}/{col}_{row}.jpeg
        private static bool TryParseDeepZoom(string expression, out (int level, int col, int row, string format) result)
        {
            if (expression.Length < 4 || expression[0] != '/')
            {
                result = default;
                return false;
            }

            // seg: {level}/{col}_{row}.jpeg
            StringSegment seg = new StringSegment(expression, 1, expression.Length - 1);
            int iPos = seg.IndexOf('/');
            if (iPos <= 0)
            {
                result = default;
                return false;
            }

            if (!int.TryParse(seg.Substring(0, iPos), out var resultLevel))
            {
                result = default;
                return false;
            }

            // seg: {col}_{row}.jpeg
            seg = seg.Subsegment(iPos + 1);
            iPos = seg.IndexOf('_');
            if (seg.IndexOf('/') >= 0 || iPos <= 0)
            {
                result = default;
                return false;
            }

            if (!int.TryParse(seg.Substring(0, iPos), out var resultCol))
            {
                result = default;
                return false;
            }

            // seg: {row}.jpeg
            seg = seg.Subsegment(iPos + 1);
            iPos = seg.IndexOf('.');
            if (iPos <= 0)
            {
                result = default;
                return false;
            }

            if (!int.TryParse(seg.Substring(0, iPos), out var resultRow))
            {
                result = default;
                return false;
            }

            // seg: jpeg
            seg = seg.Subsegment(iPos + 1);

            result = (level: resultLevel, col: resultCol, row: resultRow, format: seg.ToString());
            return true;
        }
    }
}
