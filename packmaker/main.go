// packmaker 制作 PAD 用的整合包（.pack.zip）。
//
// 用户定的边界：
//   - 至少要有一个插件（组合包 spec，写进 pack.dsh.plugins[]）；
//   - 最多就是包里带哪些结构（技能 / 规则 / MCP / 命令 / 钩子 / 子代理 / 设置），勾了才写。
//
// 用法：
//
//	packmaker make --spec <spec.json> --out <library/packs 目录> [--icon <png/svg/ico>]
//
// stdout 打印结果 zip 的绝对路径；校验失败 exit 2，IO 错误 exit 1。
package main

import (
	"archive/zip"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// Structure 决定 pack.json 里写哪些可选结构块。
type Structure struct {
	Skills    bool `json:"skills"`
	Rules     bool `json:"rules"`
	Mcp       bool `json:"mcp"`
	Commands  bool `json:"commands"`
	Hooks     bool `json:"hooks"`
	Subagents bool `json:"subagents"`
	Settings  bool `json:"settings"`
}

// Spec 是制作整合包的输入（PAD 生成一份临时 spec 调这个 exe）。
type Spec struct {
	Name        string    `json:"name"`
	Version     string    `json:"version"`
	Description string    `json:"description"`
	Kernel      string    `json:"kernel"`
	Profile     string    `json:"profile"`
	Plugins     []string  `json:"plugins"`
	Structure   Structure `json:"structure"`
	Icon        string    `json:"icon"`
	// 没上架 npm 的插件：目录也塞进包（plugins/<safe>/），清单里带 local 指路。
	LocalPlugins []LocalPlugin `json:"localPlugins"`
}

// LocalPlugin 是一个不能从 npm 装的插件目录。
type LocalPlugin struct {
	Spec string `json:"spec"`
	Dir  string `json:"dir"`
}

// ValidationError 是可给用户直接看的中文错误（exit 2）。
type ValidationError struct{ Msg string }

func (e *ValidationError) Error() string { return e.Msg }

func (s *Spec) normalize() {
	s.Name = strings.TrimSpace(s.Name)
	s.Version = strings.TrimSpace(s.Version)
	s.Description = strings.TrimSpace(s.Description)
	s.Kernel = strings.TrimSpace(s.Kernel)
	s.Profile = strings.TrimSpace(s.Profile)
	if s.Profile == "" {
		s.Profile = "web"
	}
	seen := map[string]bool{}
	out := make([]string, 0, len(s.Plugins))
	for _, p := range s.Plugins {
		p = strings.TrimSpace(p)
		if p == "" || seen[strings.ToLower(p)] {
			continue
		}
		seen[strings.ToLower(p)] = true
		out = append(out, p)
	}
	s.Plugins = out
	// 本地插件目录：去空、去重（同 spec 只留一个）。
	locals := make([]LocalPlugin, 0, len(s.LocalPlugins))
	seenLocal := map[string]bool{}
	for _, local := range s.LocalPlugins {
		local.Spec = strings.TrimSpace(local.Spec)
		local.Dir = strings.TrimSpace(local.Dir)
		if local.Dir == "" || seenLocal[strings.ToLower(local.Spec+local.Dir)] {
			continue
		}
		seenLocal[strings.ToLower(local.Spec+local.Dir)] = true
		locals = append(locals, local)
	}
	s.LocalPlugins = locals
}

// validate 落实「至少一个插件」这条最低门槛。
func (s *Spec) validate() error {
	if s.Name == "" {
		return &ValidationError{"整合包没写名字（spec.name）"}
	}
	if s.Version == "" {
		return &ValidationError{"整合包没写版本（spec.version）"}
	}
	if s.Kernel == "" {
		return &ValidationError{"整合包没写 dsh.version（内核，spec.kernel）；DSH import 必须要它"}
	}
	if len(s.Plugins)+len(s.LocalPlugins) == 0 {
		return &ValidationError{"整合包至少要有一个插件（spec.plugins 或 localPlugins）"}
	}
	return nil
}

func (s *Spec) packJSON() ([]byte, error) {
	plugins := make([]map[string]any, 0, len(s.Plugins)+len(s.LocalPlugins))
	for _, p := range s.Plugins {
		plugins = append(plugins, map[string]any{"spec": p, "required": true})
	}
	// 没上架的插件：清单里写 local 指向包内目录，装的时候从解压出来的目录 add。
	for _, local := range s.LocalPlugins {
		spec := strings.TrimSpace(local.Spec)
		if spec == "" {
			spec = filepath.Base(strings.TrimSpace(local.Dir))
		}
		plugins = append(plugins, map[string]any{
			"spec":     spec,
			"required": true,
			"local":    "plugins/" + safeFileName(spec),
		})
	}
	// DSH 投影要求包至少带一个 mod（skill/MCP/rule/command/hook）；只有组合包（plugins）
	// 的包会被 "has no mods" 拒绝，实例建不出来。自动塞一个说明用 skill 当锚点。
	const infoSkill = "pack-info"
	infoDoc := fmt.Sprintf("---\nname: %s\ndescription: 整合包 %s %s\n---\n\n# %s\n\n%s\n",
		infoSkill, s.Name, s.Version, s.Name, s.Description)
	bundleFiles := []any{map[string]any{
		"path":    "skills/" + infoSkill + "/SKILL.md",
		"content": infoDoc,
	}}
	skills := []any{map[string]any{
		"name":        infoSkill,
		"source":      "bundled",
		"version":     s.Version,
		"description": s.Description,
	}}
	doc := map[string]any{
		"schema":      "ccui-pack/v0.2",
		"name":        s.Name,
		"version":     s.Version,
		"description": s.Description,
		"dsh": map[string]any{
			"version": s.Kernel,
			"profile": s.Profile,
			"plugins": plugins,
		},
		"knowledge": map[string]any{"skills": skills},
		"modules": map[string]bool{
			"skills": true, "rules": s.Structure.Rules,
			"mcp": s.Structure.Mcp, "commands": s.Structure.Commands,
			"hooks": s.Structure.Hooks, "subagents": s.Structure.Subagents,
			"settings": s.Structure.Settings, "experiences": false,
		},
		"bundle": map[string]any{"portable": true, "files": bundleFiles},
		"meta": map[string]any{
			"author":     "PAD",
			"exportedAt": time.Now().UTC().Format("2006-01-02T15:04:05.000Z"),
			"fidelity":   "L1",
			"portable":   true,
		},
	}
	// 可选结构：勾了才写对应块（空数组占位，之后往包里补内容）。
	if s.Structure.Rules {
		doc["knowledge"] = map[string]any{"skills": skills, "rules": []any{}}
	}
	if s.Structure.Mcp {
		doc["tools"] = map[string]any{"mcp": []any{}}
	}
	if s.Structure.Commands {
		doc["commands"] = map[string]any{"files": []any{}}
	}
	if s.Structure.Hooks {
		doc["automation"] = map[string]any{"hooks": []any{}}
	}
	if s.Structure.Subagents {
		doc["agents"] = map[string]any{"subagents": []any{}}
	}
	if s.Structure.Settings {
		doc["settings"] = map[string]any{"fragments": []any{}}
	}
	return json.MarshalIndent(doc, "", "  ")
}

// Make 生成 <outDir>/<name>-<version>.pack.zip，返回绝对路径。
func Make(spec Spec, outDir, iconOverride string) (string, error) {
	spec.normalize()
	if err := spec.validate(); err != nil {
		return "", err
	}
	icon := strings.TrimSpace(iconOverride)
	if icon == "" {
		icon = strings.TrimSpace(spec.Icon)
	}
	if err := os.MkdirAll(outDir, 0o755); err != nil {
		return "", fmt.Errorf("建输出目录失败: %w", err)
	}
	zipPath := filepath.Join(outDir, safeFileName(spec.Name+"-"+spec.Version)+".pack.zip")
	body, err := spec.packJSON()
	if err != nil {
		return "", err
	}
	f, err := os.Create(zipPath)
	if err != nil {
		return "", fmt.Errorf("写 %s 失败: %w", zipPath, err)
	}
	defer f.Close()
	zw := zip.NewWriter(f)
	if err := addBytes(zw, "pack.json", body); err != nil {
		return "", err
	}
	if icon != "" {
		data, err := os.ReadFile(icon)
		if err != nil {
			return "", fmt.Errorf("读图标 %s 失败: %w", icon, err)
		}
		ext := strings.ToLower(filepath.Ext(icon))
		if ext == "" || len(ext) > 6 {
			ext = ".png"
		}
		if err := addBytes(zw, "icon"+ext, data); err != nil {
			return "", err
		}
	}
	// 没上架 npm 的插件目录：整个塞进 plugins/<safe>/。
	for _, local := range spec.LocalPlugins {
		dir := strings.TrimSpace(local.Dir)
		if dir == "" {
			continue
		}
		name := strings.TrimSpace(local.Spec)
		if name == "" {
			name = filepath.Base(dir)
		}
		if err := addDir(zw, dir, "plugins/"+safeFileName(name)+"/"); err != nil {
			return "", err
		}
	}
	if err := zw.Close(); err != nil {
		return "", fmt.Errorf("收尾 zip 失败: %w", err)
	}
	return zipPath, nil
}

func addBytes(zw *zip.Writer, name string, data []byte) error {
	w, err := zw.Create(name)
	if err != nil {
		return err
	}
	_, err = w.Write(data)
	return err
}

// addDir 把一个本地插件目录原样写进 zip（前缀 plugins/<safe>/）。
func addDir(zw *zip.Writer, root, prefix string) error {
	info, err := os.Stat(root)
	if err != nil || !info.IsDir() {
		return fmt.Errorf("本地插件目录不存在: %s", root)
	}
	return filepath.WalkDir(root, func(path string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		rel, err := filepath.Rel(root, path)
		if err != nil || rel == "." {
			return nil
		}
		name := prefix + strings.ReplaceAll(rel, "\\", "/")
		if d.IsDir() {
			_, err := zw.Create(name + "/")
			return err
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		return addBytes(zw, name, data)
	})
}

func safeFileName(s string) string {
	return strings.Map(func(r rune) rune {
		switch r {
		case '<', '>', ':', '"', '/', '\\', '|', '?', '*', '\r', '\n', '\t':
			return '_'
		}
		return r
	}, strings.TrimSpace(s))
}

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "用法: packmaker make --spec <spec.json> --out <dir> [--icon file]")
		os.Exit(2)
	}
	if os.Args[1] != "make" {
		fmt.Fprintf(os.Stderr, "未知子命令 %q（只有 make）\n", os.Args[1])
		os.Exit(2)
	}
	fs := flag.NewFlagSet("make", flag.ContinueOnError)
	specPath := fs.String("spec", "", "spec.json 路径")
	outDir := fs.String("out", "", "输出目录（PAD 的 library/packs）")
	icon := fs.String("icon", "", "覆盖 spec 里的图标")
	if err := fs.Parse(os.Args[2:]); err != nil {
		os.Exit(2)
	}
	if *specPath == "" || *outDir == "" {
		fmt.Fprintln(os.Stderr, "make 需要 --spec 和 --out")
		os.Exit(2)
	}
	spec, err := readSpec(*specPath)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(2)
	}
	path, err := Make(spec, *outDir, *icon)
	if err != nil {
		var ve *ValidationError
		if errors.As(err, &ve) {
			fmt.Fprintln(os.Stderr, "校验失败:", ve.Msg)
			os.Exit(2)
		}
		fmt.Fprintln(os.Stderr, "制作失败:", err)
		os.Exit(1)
	}
	fmt.Println(path)
}

func readSpec(path string) (Spec, error) {
	var spec Spec
	data, err := os.ReadFile(path)
	if err != nil {
		return spec, fmt.Errorf("读 spec 失败: %w", err)
	}
	if err := json.Unmarshal(data, &spec); err != nil {
		return spec, fmt.Errorf("spec 不是合法 JSON: %w", err)
	}
	return spec, nil
}
