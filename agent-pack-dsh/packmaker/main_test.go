package main

import (
	"archive/zip"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func readZipJSON(t *testing.T, zipPath, entry string) map[string]any {
	t.Helper()
	zr, err := zip.OpenReader(zipPath)
	if err != nil {
		t.Fatalf("open zip: %v", err)
	}
	defer zr.Close()
	for _, f := range zr.File {
		if f.Name != entry {
			continue
		}
		rc, err := f.Open()
		if err != nil {
			t.Fatalf("open %s: %v", entry, err)
		}
		defer rc.Close()
		data, err := io.ReadAll(rc)
		if err != nil {
			t.Fatalf("read %s: %v", entry, err)
		}
		var doc map[string]any
		if err := json.Unmarshal(data, &doc); err != nil {
			t.Fatalf("%s 不是合法 JSON: %v", entry, err)
		}
		return doc
	}
	t.Fatalf("zip 里没有 %s", entry)
	return nil
}

func baseSpec() Spec {
	return Spec{
		Name:    "demo-pack",
		Version: "0.1.0",
		Kernel:  "0.1.0-rc.7",
		Plugins: []string{"@deepseek-harness-tui/dsh-tui"},
	}
}

func TestMakeRequiresPlugin(t *testing.T) {
	spec := baseSpec()
	spec.Plugins = nil
	_, err := Make(spec, t.TempDir(), "")
	if err == nil {
		t.Fatal("没有插件也应该拒绝")
	}
	var ve *ValidationError
	if !asValidation(err, &ve) || !strings.Contains(ve.Msg, "插件") {
		t.Fatalf("错误应是「至少一个插件」，得到 %v", err)
	}
}

func asValidation(err error, target **ValidationError) bool {
	ve, ok := err.(*ValidationError)
	if ok {
		*target = ve
	}
	return ok
}

func TestMakePackContents(t *testing.T) {
	out := t.TempDir()
	spec := baseSpec()
	spec.Structure = Structure{Mcp: true}
	zipPath, err := Make(spec, out, "")
	if err != nil {
		t.Fatalf("make: %v", err)
	}
	if !strings.HasSuffix(zipPath, "demo-pack-0.1.0.pack.zip") {
		t.Fatalf("文件名不对: %s", zipPath)
	}
	doc := readZipJSON(t, zipPath, "pack.json")
	dsh := doc["dsh"].(map[string]any)
	if dsh["version"] != "0.1.0-rc.7" || dsh["profile"] != "web" {
		t.Fatalf("dsh 层不对: %#v", dsh)
	}
	plugins := dsh["plugins"].([]any)
	if len(plugins) != 1 {
		t.Fatalf("插件数不对: %#v", plugins)
	}
	if plugins[0].(map[string]any)["spec"] != "@deepseek-harness-tui/dsh-tui" {
		t.Fatalf("插件 spec 不对: %#v", plugins[0])
	}
	if _, ok := doc["tools"]; !ok {
		t.Fatal("勾了 MCP 就应该有 tools.mcp")
	}
	// 投影要求至少一个 mod：自动带的 pack-info skill 必须存在（否则 "has no mods" 建不了实例）。
	knowledge, ok := doc["knowledge"].(map[string]any)
	if !ok {
		t.Fatal("包里没有 knowledge（投影会报 has no mods）")
	}
	skills, ok := knowledge["skills"].([]any)
	if !ok || len(skills) == 0 {
		t.Fatalf("包里没有自带 skill mod: %#v", knowledge)
	}
	if _, ok := doc["commands"]; ok {
		t.Fatal("没勾命令就不该有 commands")
	}
}

func TestMakeEmbedsIcon(t *testing.T) {
	dir := t.TempDir()
	icon := filepath.Join(dir, "logo.png")
	if err := os.WriteFile(icon, []byte("\x89PNG fake"), 0o644); err != nil {
		t.Fatal(err)
	}
	spec := baseSpec()
	spec.Icon = icon
	zipPath, err := Make(spec, dir, "")
	if err != nil {
		t.Fatalf("make: %v", err)
	}
	zr, err := zip.OpenReader(zipPath)
	if err != nil {
		t.Fatal(err)
	}
	defer zr.Close()
	found := false
	for _, f := range zr.File {
		if f.Name == "icon.png" {
			found = true
		}
	}
	if !found {
		t.Fatal("图标没写进 zip 根")
	}
}

func TestMakeBundlesLocalPlugins(t *testing.T) {
	dir := t.TempDir()
	pluginDir := filepath.Join(dir, "my-local-plugin")
	if err := os.MkdirAll(filepath.Join(pluginDir, "lib"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(pluginDir, "package.json"), []byte(`{"name":"my-local-plugin","version":"1.2.3"}`), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(pluginDir, "lib", "index.js"), []byte("export {}"), 0o644); err != nil {
		t.Fatal(err)
	}
	spec := baseSpec()
	spec.Plugins = nil
	spec.LocalPlugins = []LocalPlugin{{Spec: "my-local-plugin", Dir: pluginDir}}
	out := filepath.Join(dir, "out")
	zipPath, err := Make(spec, out, "")
	if err != nil {
		t.Fatalf("make: %v", err)
	}
	zr, err := zip.OpenReader(zipPath)
	if err != nil {
		t.Fatal(err)
	}
	defer zr.Close()
	hasLib := false
	for _, f := range zr.File {
		if f.Name == "plugins/my-local-plugin/lib/index.js" {
			hasLib = true
		}
	}
	if !hasLib {
		t.Fatal("本地插件目录没塞进 plugins/")
	}
	doc := readZipJSON(t, zipPath, "pack.json")
	dsh := doc["dsh"].(map[string]any)
	plugins := dsh["plugins"].([]any)
	entry := plugins[0].(map[string]any)
	if entry["local"] != "plugins/my-local-plugin" {
		t.Fatalf("清单里没有 local 指路: %#v", entry)
	}
}
func TestPluginDedupAndTrim(t *testing.T) {
	spec := baseSpec()
	spec.Plugins = []string{"  @deepseek-harness-tui/dsh-tui ", "@DEEPSEEK-HARNESS-TUI/dsh-tui", "", "@sakikotgw/pad-gateway"}
	spec.normalize()
	if len(spec.Plugins) != 2 {
		t.Fatalf("重复/空白插件没清干净: %#v", spec.Plugins)
	}
}
