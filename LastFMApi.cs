using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace EpikLastFMApi
{
    class LastFMApi
    {
        public const string BaseURL = "https://ws.audioscrobbler.com/2.0/";

        private readonly string _key;

        public LastFMApi(string key)
        {
            _key = key;
        }

        public async Task<string> AlbumSearchAsync(Func<JObject, string, string, string> findValue, string album, string artist = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(album))
                    return null;

                string url = $"{BaseURL}?method=album.search&album={_uriEnc(album)}";
                JObject json = await _jsonResponseAsync(url);

                return findValue(json, artist, album);
            }
            catch
            { 
                return null;
            }
        }

        public async Task<string> AlbumGetInfoAsync(Func<JObject, string> findValue, string album, string artist = "", string track = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(album) || string.IsNullOrWhiteSpace(artist))
                    return null;

                string url = $"{BaseURL}?method=album.getinfo&album={_uriEnc(album)}";

                if (!string.IsNullOrWhiteSpace(artist))
                    url += $"&artist={_uriEnc(artist)}";
                if (!string.IsNullOrWhiteSpace(track))
                    url += $"&track={_uriEnc(track)}";

                JObject json = await _jsonResponseAsync(url);

                return findValue(json);
            }
            catch
            {
                return null;
            }
        }

        private async Task<JObject> _jsonResponseAsync(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage resp = await client.GetAsync(url + $"&api_key={_key}&format=json");

                if (resp.IsSuccessStatusCode)
                    return JObject.Parse(await resp.Content.ReadAsStringAsync());
                else
                    throw new HttpRequestException();
            }
        }

        private string _uriEnc(string a) => Uri.EscapeDataString(a);
    }
}
