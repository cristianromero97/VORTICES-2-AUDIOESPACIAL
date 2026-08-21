using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Vuplex.WebView;

//This script applies spatial audio to gallery objects, and deletes or comments to revert to the previous version.
namespace Vortices
{
    public class MuseumElementSpatialAudio : MonoBehaviour
    {
        private CanvasWebViewPrefab _webViewPrefab;
        private bool _spatialEnabled;
        private float _refDistance;
        private float _maxDistance;
        private string _distanceModel;
        private float _updateTimer;
        private const float UpdateInterval = 0.2f;
        private string _currentFilePath;
        private int _loadSeq;
        private const long MaxBase64Bytes = 50L * 1024 * 1024; // 50 MB

        private static readonly HashSet<string> AudioVideoExts = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".aac", ".flac", ".m4a", ".opus", ".weba", ".wma",
            ".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".ogv"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableFileAccess()
        {
            StandaloneWebView.SetCommandLineArguments(
                "--allow-file-access-from-files --autoplay-policy=user-gesture-required");
        }

        // PannerNode: HRTF + atenuación natural (rolloffFactor=1).
        // GainNode (_sg): corte binario — fuerza gain=0 más allá de maxDistance para eliminar el piso ~8% del modelo inverse.
        private const string SetupJSTemplate =
@"(function(){
if(window._sc)return;
var ctx=new(window.AudioContext||window.webkitAudioContext)();
var p=ctx.createPanner();
p.panningModel='HRTF';
p.distanceModel='__DM__';
p.refDistance=__RD__;
p.maxDistance=__MD__;
p.rolloffFactor=1;
var g=ctx.createGain();
g.gain.value=1;
p.connect(g);
g.connect(ctx.destination);
function play(el){
stop(el);if(!el._buf)return;
var t=el.currentTime;el.muted=true;
ctx.resume().then(function(){
var s=ctx.createBufferSource();
s.buffer=el._buf;s.loop=el.loop;s.connect(p);
s.start(0,Math.min(Math.max(0,t),el._buf.duration-0.001));
el._src=s;
//console.log('[SA] hrtf playing dur='+el._buf.duration.toFixed(1));
});
}
function stop(el){if(el._src){try{el._src.stop();}catch(e){}el._src=null;}}
function setup(el){
if(el._done)return;el._done=true;
el.autoplay=false;if(!el.paused)el.pause();
window._el=el;
if(window._pendingB64){var b=window._pendingB64;window._pendingB64=null;window._loadAudio(b);}
function go(){
var url=el.currentSrc||el.src;
if(!url||url.startsWith('blob:')||url.startsWith('data:')){
el.setAttribute('data-vol','');return;
}
var x=new XMLHttpRequest();
x.open('GET',url,true);x.responseType='arraybuffer';
x.onload=function(){
var bytes=x.response?x.response.byteLength:0;
if(!x.response||bytes===0){el.setAttribute('data-vol','');return;}
ctx.decodeAudioData(x.response,function(buf){
el._buf=buf;
if(!el.paused||el._pp){play(el);el._pp=false;}
},function(){el.setAttribute('data-vol','');});
};
x.onerror=function(){el.setAttribute('data-vol','');};
x.send();
}
if(el.readyState>=1)go();else el.addEventListener('loadstart',go,{once:true});
el.addEventListener('play',function(){if(el._buf)play(el);else el._pp=true;});
el.addEventListener('pause',function(){stop(el);if(el._buf)el.muted=false;});
el.addEventListener('seeked',function(){if(el._buf&&!el.paused){stop(el);play(el);}});
el.addEventListener('ended',function(){stop(el);el.muted=false;});
}
function all(){document.querySelectorAll('audio,video').forEach(setup);}
all();
document.querySelectorAll('audio,video').forEach(function(el){el.autoplay=false;if(!el.paused)el.pause();});
new MutationObserver(all).observe(document,{childList:true,subtree:true});
document.addEventListener('click',function(){ctx.resume();},{capture:true});
window._sc=ctx;window._sp=p;window._sg=g;
window._loadAudio=function(b64){
if(!window._el){window._pendingB64=b64;/*console.log('[SA] b64 queued');*/return;}
try{
var bin=atob(b64);
var a=new ArrayBuffer(bin.length);
var v=new Uint8Array(a);
for(var i=0;i<bin.length;i++)v[i]=bin.charCodeAt(i);
ctx.decodeAudioData(a,function(buf){
window._el._buf=buf;
//console.log('[SA] b64 ok dur='+buf.duration.toFixed(1)+'s');
if(!window._el.paused||window._el._pp){window._el._pp=false;play(window._el);}
},function(){/*console.log('[SA] b64 decode fail');*/window._el.setAttribute('data-vol','');});
}catch(e){/*console.log('[SA] _loadAudio err: '+e.message);*/}
};
//console.log('[SA] init dm=__DM__ ref=__RD__ max=__MD__');
})();";

        public void SetWebView(CanvasWebViewPrefab webViewPrefab, string filePath = "")
        {
            if (_webViewPrefab != null && _webViewPrefab.WebView != null)
            {
                _webViewPrefab.WebView.LoadProgressChanged -= OnLoadProgress;
                _webViewPrefab.WebView.ConsoleMessageLogged -= OnWebViewConsole;
            }

            _webViewPrefab = webViewPrefab;
            _currentFilePath = filePath;
            ReadAudioConfig();

            if (_webViewPrefab == null || !_spatialEnabled) return;

            _webViewPrefab.WebView.LoadProgressChanged += OnLoadProgress;
            _webViewPrefab.WebView.ConsoleMessageLogged += OnWebViewConsole;
            _ = SetupAsync(++_loadSeq, filePath);
        }

        private void OnWebViewConsole(object _, ConsoleMessageEventArgs __)
        {
            //if (__.Message != null && __.Message.Contains("[SA]"))
            //    Debug.Log($"[WebView] {__.Level}: {__.Message}");
        }

        private void ReadAudioConfig()
        {
            _spatialEnabled = false;

            SessionManager sm = SessionManager.instance;
            int level = sm != null ? sm.configLevel : 0;

            if (level <= 0 && AudioManager.Instance != null)
                level = AudioManager.Instance.CurrentConfigLevel;

            if (level <= 0) return;

            AudioManager.AcousticProfile profile = AudioManager.Instance?.GetAcousticProfile(level);
            if (profile != null)
            {
                if (profile.spatialBlend <= 0f) return;
                _distanceModel = profile.rolloffMode == AudioRolloffMode.Linear ? "linear" : "inverse";
            }
            else
            {
                if (level < 3) return;
                _distanceModel = "inverse";
            }

            _spatialEnabled = true;

            if (sm != null && sm.hasEmitterOverride)
            {
                _refDistance = sm.emitterMinDistance;
                _maxDistance = sm.emitterMaxDistance;
            }
            else
            {
                _refDistance = 2f;
                _maxDistance = 12f;
            }
        }

        private string BuildSetupJS() =>
            SetupJSTemplate
                .Replace("__DM__", _distanceModel)
                .Replace("__RD__", _refDistance.ToString("F2"))
                .Replace("__MD__", _maxDistance.ToString("F2"));

        private async Task SetupAsync(int seq, string filePath)
        {
            if (_webViewPrefab == null || _webViewPrefab.WebView == null) return;
            await _webViewPrefab.WebView.ExecuteJavaScript(BuildSetupJS());
            if (seq != _loadSeq) return;
            if (IsLocalFile(filePath) && IsAudioVideoFile(filePath))
                await InjectFileDataAsync(seq, filePath);
        }

        private async Task InjectFileDataAsync(int seq, string filePath)
        {
            string localPath = ToLocalPath(filePath);
            if (!File.Exists(localPath))
            {
                //Debug.Log($"[SA] file not found: {localPath}");
                return;
            }

            var info = new FileInfo(localPath);
            if (info.Length > MaxBase64Bytes)
            {
                //Debug.Log($"[SA] file too large ({info.Length / 1024 / 1024}MB) → vol fallback");
                return;
            }

            byte[] bytes;
            try { bytes = await Task.Run(() => File.ReadAllBytes(localPath)); }
            catch (Exception) { return; }

            if (seq != _loadSeq) return;

            string b64 = await Task.Run(() => Convert.ToBase64String(bytes));

            if (seq != _loadSeq) return;
            if (_webViewPrefab == null || _webViewPrefab.WebView == null) return;

            await _webViewPrefab.WebView.ExecuteJavaScript(
                $"if(window._loadAudio)window._loadAudio('{b64}');");

            //Debug.Log($"[SA] injected {info.Length / 1024}KB for {Path.GetFileName(localPath)}");
        }

        private static bool IsLocalFile(string path) =>
            !string.IsNullOrEmpty(path) &&
            (path.StartsWith("file:///") ||
             path.StartsWith("file://") ||
             (!path.StartsWith("http://") && !path.StartsWith("https://") && File.Exists(path)));

        private static bool IsAudioVideoFile(string path)
        {
            string ext = Path.GetExtension(ToLocalPath(path));
            return AudioVideoExts.Contains(ext);
        }

        private static string ToLocalPath(string path)
        {
            if (path.StartsWith("file:///"))
            {
                string decoded = Uri.UnescapeDataString(path.Substring(8));
                return decoded.Replace('/', Path.DirectorySeparatorChar);
            }
            if (path.StartsWith("file://"))
            {
                string decoded = Uri.UnescapeDataString(path.Substring(7));
                return decoded.Replace('/', Path.DirectorySeparatorChar);
            }
            return path;
        }

        private async void OnLoadProgress(object sender, ProgressChangedEventArgs e)
        {
            if (e.Type != ProgressChangeType.Finished) return;
            if (_webViewPrefab == null || _webViewPrefab.WebView == null) return;
            int seq = _loadSeq;
            await _webViewPrefab.WebView.ExecuteJavaScript(BuildSetupJS());
            if (seq != _loadSeq) return;
            if (IsLocalFile(_currentFilePath) && IsAudioVideoFile(_currentFilePath))
                await InjectFileDataAsync(seq, _currentFilePath);
        }

        private void Update()
        {
            if (!_spatialEnabled || _webViewPrefab == null) return;
            _updateTimer += Time.deltaTime;
            if (_updateTimer < UpdateInterval) return;
            _updateTimer = 0f;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_webViewPrefab.WebView == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 diff = transform.position - cam.transform.position;
            Vector3 local = cam.transform.InverseTransformDirection(diff);
            float x = local.x;
            float y = local.y;
            float z = -local.z;

            float dist = diff.magnitude;

            // vol suave para fallback (element.volume) — llega a 0 en maxDistance
            float vol;
            if (dist <= _refDistance) vol = 1f;
            else if (dist >= _maxDistance) vol = 0f;
            else if (_distanceModel == "linear")
                vol = 1f - (dist - _refDistance) / (_maxDistance - _refDistance);
            else
                vol = _refDistance / dist;

            // cutoff binario para el GainNode del path HRTF:
            // PannerNode (rolloffFactor=1) ya hace atenuación natural;
            // el GainNode solo fuerza silencio más allá de maxDistance (elimina el piso ~8%).
            float cutoff = dist >= _maxDistance ? 0f : 1f;

            string js =
                $"if(window._sp){{window._sp.positionX.value={x:F2};window._sp.positionY.value={y:F2};window._sp.positionZ.value={z:F2};" +
                $"if(window._sc&&window._sc.state==='suspended')window._sc.resume();}}" +
                $"if(window._sg)window._sg.gain.value={cutoff:F1};" +
                $"if(window._el&&!window._el._buf){{window._el.volume={vol:F2};" +
                $"document.querySelectorAll('audio[data-vol],video[data-vol]').forEach(function(e){{e.volume={vol:F2};}});}}";

            _webViewPrefab.WebView.ExecuteJavaScript(js);
        }

        private void OnDestroy()
        {
            if (_webViewPrefab != null && _webViewPrefab.WebView != null)
            {
                _webViewPrefab.WebView.LoadProgressChanged -= OnLoadProgress;
                _webViewPrefab.WebView.ConsoleMessageLogged -= OnWebViewConsole;
            }
        }
    }
}
