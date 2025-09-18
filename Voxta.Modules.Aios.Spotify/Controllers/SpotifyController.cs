using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Voxta.Modules.Aios.Spotify.Clients.Services;

namespace Voxta.Modules.Aios.Spotify.Controllers;

[Route("api/extensions/spotify")]
public class SpotifyController : Controller
{
    [HttpGet("oauth2/callback")]
    [AllowAnonymous]
    public IActionResult Test(
        [FromQuery] string code,
        [FromServices] ISpotifyAuthCallbackManager spotifyAuthCallbackManager
        )
    {
        if (string.IsNullOrEmpty(code))
            return BadRequest("Missing code");

        spotifyAuthCallbackManager.Callback(code);
        
        return Content(
            // language=html
            """
            <html>
            <head>
                <style>
                    body {
                        background-color: #121212;
                        color: #ffffff;
                        font-family: Arial, sans-serif;
                        height: 100vh;
                        margin: 0;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        text-align: center;
                    }
                </style>
            </head>
            <body>
                <div>
                    Authentication successful! You can close this tab.
                </div>
            </body>
            </html>
            """,
            "text/html"
        );
    }
}
