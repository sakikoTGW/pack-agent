export type { PackDoc, InstallOpts, InstallReport, RuntimeInstallReport, CaptureDeliver, PackExperience, PackPolicy, PackAgentRef } from './types.js'

export { DEFAULT_STATE_DIR, PACK_APPLY_SKIP, projectPackToRuntimes, resolveApplyRuntimes } from './project.js'

export type { PackProjectOpts, PackProjectManifest, RuntimeProjectReport } from './project.js'

export type { ConflictPolicy } from './types.js'
export { PackConflictError, formatPackConflict, conflictPayload, CONFLICT_POLICIES } from './errors.js'

export { installPack, installPackFile } from './install.js'

export {

  RUNTIME_ADAPTERS,

  detectRuntimes,

  getAdapter,

  runtimeProjectionDirs,

  scanRuntime,

  scanUniversal,

} from './adapters.js'

export type { RuntimeAdapter, RuntimeScan } from './adapters.js'

export { mergeMcp, unmergeMcp, mcpTargetFor, addHermesExternalDir, removeHermesExternalDir } from './projection.js'

export type { McpFormat, McpTarget, McpServers } from './projection.js'

export { injectBootstrapIntoPack, resolveAgentPackSkillDir, BOOTSTRAP_SKILL_NAME } from './bootstrap.js'

export { enrichPackVersions, PACK_SCHEMA_V02, parseSkillFrontmatter } from './versioning.js'

export { loadPackProjectConfig, ensureAgentPackProjectYaml, type PackProjectConfig } from './project-config.js'

export { writePackLock, readPackLock, packToLock, type PackLock } from './lock.js'

export { syncPack, type SyncOpts, type SyncReport } from './sync.js'

export { buildPackFromProject, exportPackFromProject, type ExportOpts } from './export.js'

export {
  installExperiences,
  mergeExperiencesFromCapture,
  resolveCaptureDeliver,
  captureDocToExperience,
  applyExperienceOffset,
} from './experience.js'

export { distillTranscriptsToExperiences, distillTranscriptContent } from './transcript-distill.js'

export { ejectPack, packStatus, type EjectReport, type EjectItemStatus } from './eject.js'
export { buildInstallLedger, readInstallLedger, writeInstallLedger } from './install-ledger.js'
export { validateSkillRequires, validatePackSkillsResolvable } from './requires-check.js'
export { skillOriginInBundle, skillHasBundleFiles } from './portable.js'
export { bootstrapAgentPackMcp, removeAgentPackMcpBootstrap } from './mcp-bootstrap.js'
export { unwireExperienceHooks } from './experience-projection.js'

export {
  projectExperienceToHarnesses,
  ensureExperienceHookScript,
  EXPERIENCE_INJECT_SLOTS,
  resolveExperienceRuntimes,
  validateExperienceAdapterCoverage,
} from './experience-projection.js'
export type { ExperienceProjectionReport, ExperienceInjectSlot, ExperienceInjectKind } from './experience-projection.js'

export { loadExperienceInjection, mergeSystemPromptWithExperiences, type ExperienceInjection } from './experience-loader.js'

export {
  loadPackIgnore,
  parsePackIgnore,
  isPackIgnored,
  ensureDefaultPackIgnore,
  DEFAULT_PACK_IGNORE_TEMPLATE,
} from './pack-ignore.js'

export {
  DEFAULT_PACK_MODULES,
  resolvePackModules,
  filterPackByModules,
  parseModulesList,
  type PackModuleId,
  type PackModules,
} from './modules.js'

export { scanExtendedModules, mergeExtendedIntoPack } from './scan-modules.js'

export { installExtendedModules } from './install-modules.js'

export { diffLockFiles, diffPackFiles, formatDiffReport, type PackDiffReport } from './diff.js'

export {

  embedPortableFiles,

  materializePortableBundle,

  resolveSkillDir,

  resolveRuleFile,

  readPackFile,

} from './portable.js'

export { writeAstrbotPlugin } from './astrbot.js'

export {
  loadAgentsRegistry,
  listAgentProfiles,
  getAgentProfile,
  resolveAgentForExport,
  ensureAgentsYamlTemplate,
  agentsYamlPath,
  type AgentProfile,
  type AgentsRegistry,
} from './agents.js'

export { createServer as createAgentPackMcpServer } from '../mcp/server.js'

