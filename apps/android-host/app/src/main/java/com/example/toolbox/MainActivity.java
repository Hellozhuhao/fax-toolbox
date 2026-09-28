package com.example.toolbox;

import android.app.Activity;
import android.net.Uri;
import android.os.Bundle;
import android.view.View;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.webkit.WebViewAssetLoader;
import androidx.webkit.WebViewClientCompat;
import androidx.webkit.WebViewCompat;
import androidx.webkit.WebViewFeature;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.Set;

public final class MainActivity extends Activity {
    private static final String ORIGIN = "https://appassets.androidplatform.net";
    private static final String PAGE = ORIGIN + "/assets/smoke.html";
    private static final Set<String> ALLOWED_ORIGINS = Collections.singleton(ORIGIN);
    private static final int MAX_MESSAGE_LENGTH = 16384;

    static { System.loadLibrary("toolbox_core_jni"); }

    private static native long nativeCreate();
    private static native void nativeDestroy(long handle);
    private static native int nativeDispatch(long handle, int event, long sequence);

    private WebView webView;
    private TextView status;
    private long core;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (BuildConfig.DEBUG) WebView.setWebContentsDebuggingEnabled(true);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        Button reload = new Button(this);
        reload.setText("重新运行");
        status = new TextView(this);
        status.setPadding(16, 8, 16, 8);
        status.setText("初始化中…");
        webView = new WebView(this);
        layout.addView(reload);
        layout.addView(status);
        layout.addView(webView, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(layout);

        if (!WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER) ||
            !WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            status.setText("系统 WebView 不支持所需的安全消息桥或文档起始注入");
            reload.setEnabled(false);
            return;
        }

        WebViewAssetLoader loader = new WebViewAssetLoader.Builder()
            .addPathHandler("/assets/", new WebViewAssetLoader.AssetsPathHandler(this))
            .build();
        webView.getSettings().setJavaScriptEnabled(true);
        webView.getSettings().setAllowFileAccess(false);
        webView.getSettings().setAllowContentAccess(false);
        webView.setWebViewClient(new WebViewClientCompat() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                return loader.shouldInterceptRequest(request.getUrl());
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return !PAGE.equals(request.getUrl().toString());
            }
        });

        try {
            String script = readAsset("page-agent.js") + "\n" +
                "window.__toolboxAgent=ToolboxPageAgent.createAgent({" +
                "document:document,transport:ToolboxPageAgent.resolveHostTransport(window)," +
                "moduleId:'toolbox.smoke',sessionId:'starter',generation:1});" +
                "window.__toolboxAgent.start();";
            WebViewCompat.addWebMessageListener(webView, "ToolboxAndroid", ALLOWED_ORIGINS,
                (view, message, sourceOrigin, isMainFrame, replyProxy) -> {
                    if (isMainFrame && Uri.parse(ORIGIN).equals(sourceOrigin)) {
                        String data = message.getData();
                        if (data != null) runOnUiThread(() -> acceptMessage(data));
                    }
                });
            WebViewCompat.addDocumentStartJavaScript(webView, script, ALLOWED_ORIGINS);
        } catch (IOException | RuntimeException error) {
            status.setText("模块初始化失败：" + error.getMessage());
            reload.setEnabled(false);
            return;
        }

        reload.setOnClickListener(view -> startModule());
        startModule();
    }

    private String readAsset(String name) throws IOException {
        try (InputStream input = getAssets().open(name);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int count;
            while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            return output.toString(StandardCharsets.UTF_8.name());
        }
    }

    private void startModule() {
        if (core != 0) nativeDestroy(core);
        core = nativeCreate();
        if (core == 0) {
            status.setText("C++ 核心初始化失败");
            return;
        }
        status.setText("加载模块中…");
        webView.loadUrl(PAGE);
    }

    private void acceptMessage(String raw) {
        if (core == 0 || raw.length() > MAX_MESSAGE_LENGTH) return;
        try {
            JSONObject message = new JSONObject(raw);
            if (message.optInt("protocol") != 1 ||
                !"toolbox.smoke".equals(message.optString("moduleId")) ||
                !"starter".equals(message.optString("sessionId")) ||
                !"main".equals(message.optString("frameId")) ||
                message.optInt("generation") != 1) return;
            String type = message.optString("type");
            int event;
            if ("HELLO".equals(type)) event = 1;
            else if ("VIDEO_FOUND".equals(type)) event = 2;
            else if ("VIDEO_ENDED".equals(type)) event = 3;
            else return;
            long sequence = message.optLong("seq", -1);
            int state = nativeDispatch(core, event, sequence);
            if (state == 2) status.setText("已发现视频；点击页面按钮完成验证");
            else if (state == 3) status.setText("C++ 核心状态：COMPLETE");
        } catch (JSONException ignored) {
            // A malformed page message has no effect on the native core.
        }
    }

    @Override
    protected void onDestroy() {
        if (webView != null) webView.destroy();
        if (core != 0) nativeDestroy(core);
        core = 0;
        super.onDestroy();
    }
}
