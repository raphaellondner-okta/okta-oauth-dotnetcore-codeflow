﻿
//using IdentityModel.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Okta.OAuth.CodeFlow.DotNetCore.Client
{
    public class Startup
    {
        string oidcClientId = string.Empty;
        string oidcClientSecret = string.Empty;
        string oidcAuthority = string.Empty;
        string oidcRedirectUri = string.Empty;
        string oidcResponseType = string.Empty;
        bool oauthTokenEndPointBasicAuth = false;

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                //OktaDev: added config.json reference and config.json file
                //.AddJsonFile("config.json")
                .AddJsonFile($"config.{env.EnvironmentName}.json", optional: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            oidcClientId = Configuration["Okta:ClientId"] as string;
            oidcClientSecret = Configuration["Okta:ClientSecret"] as string;
            oidcAuthority = Configuration["Okta:OrganizationUri"] as string;
            bool.TryParse(Configuration["Okta:TokenEndPointBasicAuth"] as string, out oauthTokenEndPointBasicAuth);

        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            //OktaDev: add authentication services.
            services.AddAuthentication(sharedOptions => sharedOptions.SignInScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            //Add the console logger
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));

            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();


            //OktaDev: Configure the OWIN pipeline to use cookie auth.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                CookieName = "MyApp",
                AuthenticationScheme = "Cookies",
            });

            oidcClientId = Configuration["Okta:ClientId"] as string;
            oidcClientSecret = Configuration["Okta:ClientSecret"] as string;
            oidcAuthority = Configuration["Okta:OrganizationUri"] as string;
            oidcRedirectUri = Configuration["Okta:RedirectUri"] as string;
            oidcResponseType = Configuration["Okta:ResponseType"] as string;

            OpenIdConnectOptions oidcOptions = new OpenIdConnectOptions
            {
                Authority = oidcAuthority,
                ClientId = oidcClientId,
                ClientSecret = oidcClientSecret,
                //OktaDev: alternatively you can include the response type using Microsoft's library constants 
                //in Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType,
                ResponseType = oidcResponseType,
                //AuthenticationScheme is using the default value, but if you change it to something custom, you must update the AccountController.Login method as well
                AuthenticationScheme = Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
                CallbackPath = "/Callback", //OktaDev: The forward slash is implied. Note that if we don't set this value manually, the callback path is by default /signin-oidc

                //RefreshOnIssuerKeyNotFound is enabled by default and takes care of key rollover so no need to configure it here (Okta does perform key rollover unless configured otherwise)


                TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                {
                    ValidAudience = oidcClientId
                },

                Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    OnRemoteFailure = OnAuthenticationFailed,
                    //OnAuthorizationCodeReceived = ExchangeCodeWithBasicAuthentication
                },
            };

            //OktaDev: add the OIDC default scopes + the custom Okta "groups" scope (that returns the user's groups filtered or not depending on your Okta OIDC client configuration)
            //oidcOptions.Scope.Add(Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectScope.OpenIdProfile);
            oidcOptions.Scope.Add("email");
            oidcOptions.Scope.Add("phone");
            oidcOptions.Scope.Add("address");
            oidcOptions.Scope.Add("groups");
            oidcOptions.Scope.Add("offline_access");

            //OktaDev:  Configure the OWIN pipeline to use OpenID Connect authentication.
            app.UseOpenIdConnectAuthentication(oidcOptions);


            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        // OktaDev: Handle code exchange with Okta. We must override this event because the Microsoft.AspNetCore.Authentication.OpenIdConnect middleware uses only the client_post_method to handle authentication with the Token endpoint while exchanging the code for the access token
        private Task OnAuthenticationFailed(Microsoft.AspNetCore.Authentication.FailureContext context)
        {
            context.HandleResponse();
            context.Response.Redirect("/Home/Error?message=" + context.Failure.Message);
            return Task.FromResult(0);
        }


        // OktaDev: Handle sign-in errors differently than generic errors.
        /*
        private Task ExchangeCodeWithBasicAuthentication(Microsoft.AspNetCore.Authentication.OpenIdConnect.AuthorizationCodeReceivedContext ctx)
        {
            if (oauthTokenEndPointBasicAuth)
            {
                // use the code to get the access and refresh token thanks to the IdentityModel2 middleware - this is only necessary if using client_secret_basic as the authentication method for the /token endpoint since the Microsoft ASP.NET Core OpenID Connect middleware only suppoerts the client_secret_post authentication method.
                var tokenClient = new TokenClient(
                    oidcAuthority + Constants.TokenEndpoint,
                    oidcClientId,
                    oidcClientSecret, AuthenticationStyle.BasicAuthentication);

                var tokenResponse = tokenClient.RequestAuthorizationCodeAsync(ctx.TokenEndpointRequest.Code, ctx.TokenEndpointRequest.RedirectUri);


                if (tokenResponse.IsFaulted)
                {
                    throw tokenResponse.Exception;
                }

                // use the access token to retrieve claims from userinfo
                var userInfoClient = new UserInfoClient(oidcAuthority + Constants.UserInfoEndpoint);

                var userInfoResponse = userInfoClient.GetAsync(tokenResponse.Result.AccessToken);

                //// create new identity
                var id = new ClaimsIdentity(ctx.Ticket.AuthenticationScheme);
                //adding the claims we get from the userinfo endpoint
                id.AddClaims(userInfoResponse.Result.Claims);

                //also adding the ID, Access and Refresh tokens to the user claims 
                id.AddClaim(new Claim("id_token", ctx.JwtSecurityToken.RawData));
                id.AddClaim(new Claim("access_token", tokenResponse.Result.AccessToken));
                if (tokenResponse.Result.AccessToken != null)
                    id.AddClaim(new Claim("refresh_token", tokenResponse.Result.RefreshToken));

                id.AddClaim(new Claim("expires_at", DateTime.Now.AddSeconds(tokenResponse.Result.ExpiresIn).ToLocalTime().ToString()));

                //ctx.Ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(id.Claims, ctx.Ticket.Principal.Identity.AuthenticationType)),
                //    ctx.Ticket.Properties, ctx.Ticket.AuthenticationScheme);

                //Tells the OWIN middleware we retrieved the tokens on our own code, so that it doesn't try it on its own
                ctx.HandleCodeRedemption(new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectMessage()
                {
                    IdToken = ctx.JwtSecurityToken.RawData,
                    AccessToken = tokenResponse.Result.AccessToken
                });
            }
            //context.HandleResponse();
            //context.Response.Redirect("/Home/Error?message=" + context.Failure.Message);
            return Task.FromResult(0);
        }
        */
    }
}
