# voxta-providers

# Installing Providers

1. Place the provider into the *Voxta.SampleProviderApp\Providers* folder
2. Place the respective config into *Voxta.SampleProviderApp\Providers\configs* folder
3. Add each Provider to the **Program.cs** inside the *Voxta.SampleProviderApp* folder

# Spotify Provider Setup

1. Set Up Your Spotify App: Go to the Spotify Developer Dashboard: https://developer.spotify.com/dashboard
2. Create an app to get your: Client ID, Client Secret, Set a Redirect URI (for OAuth2 authentication)
3. Add those values into the *Voxta.SampleProviderApp\Providers\configs\UserFunctionProviderSpotifyConfig.json*

# Weather API Provider Setup

For this provider to work we rely on an external API service: https://openweathermap.org/. They have a free plan which is rate limited to max 60 calls per minute

1. Open the URL and register
2. Once you are registered and signed in, go to your profile and click on "My API keys"
3. Give your key a custom name and hit Generate
4. Add the API key into the *Voxta.SampleProviderApp\Providers\configs\UserFunctionProviderWeatherConfig.json*
