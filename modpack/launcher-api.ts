/**
 * Tauri / CLI 同构 API。窗口只调这些方法，不另起一套。
 */
import { adoptExistingHome } from './adopt.js'
import { analyzeCrash } from './crash.js'
import { getGlobalCredentials, getNamedCredentials, listCredentialSets, setGlobalCredentials, setNamedCredentials } from './credentials.js'
import { doctorLauncher } from './doctor.js'
import { scanDropZips } from './drop-scan.js'
import { listOrEmpty } from './empty-state.js'
import { importPack } from './import-pack.js'
import { cloneInstance, pinInstance, renameInstance, setInstanceDisplay } from './instance-ops.js'
import { cancelJob, listJobs } from './jobs.js'
import {
  createInstance,
  getInstance,
  instanceLogs,
  listInstances,
  listVersions,
  readRuntime,
  removeInstance,
  restartInstance,
  runInstance,
  stopInstance,
  type LauncherRoot,
} from './launcher.js'
import { marketInstall, marketList, marketSearch } from './market.js'
import { getMeta } from './meta-cache.js'
import { packAllow, packDeny, packList, packProject, packSetLoad, packSetSave } from './pack-ops.js'
import { exportInstance } from './pinst.js'
import {
  pluginAddToInstance,
  pluginDisable,
  pluginEnable,
  pluginList,
  pluginRemove,
  pluginUpdate,
} from './plugin-ops.js'
import { backupSessions, deleteSession, inspectSession, listSessions } from './session-ops.js'
import { writeShortcut } from './shortcut.js'
import { applyUpdate, checkUpdate } from './update.js'

export const LAUNCHER_API_METHODS = [
  'version.list',
  'instance.list',
  'instance.create',
  'instance.info',
  'instance.remove',
  'instance.clone',
  'instance.rename',
  'instance.pin',
  'instance.display',
  'import',
  'export',
  'scanDrop',
  'run',
  'stop',
  'restart',
  'logs',
  'ps',
  'plugin.list',
  'plugin.add',
  'plugin.remove',
  'plugin.update',
  'plugin.enable',
  'plugin.disable',
  'pack.list',
  'pack.project',
  'pack.allow',
  'pack.deny',
  'pack.setSave',
  'pack.setLoad',
  'session.list',
  'session.inspect',
  'session.delete',
  'session.backup',
  'job.list',
  'job.cancel',
  'crash.analyze',
  'doctor',
  'credentials.get',
  'credentials.set',
  'credentials.list',
  'market.list',
  'market.search',
  'market.install',
  'update.check',
  'update.apply',
  'shortcut.write',
  'meta.get',
  'adopt',
] as const

export type LauncherApiMethod = (typeof LAUNCHER_API_METHODS)[number]

export async function invokeLauncherApi(
  root: LauncherRoot,
  method: string,
  params: Record<string, unknown> = {},
): Promise<unknown> {
  if (!(LAUNCHER_API_METHODS as readonly string[]).includes(method)) {
    throw new Error(`unknown launcher api method: ${method}`)
  }
  const id = String(params.id || '')
  switch (method as LauncherApiMethod) {
    case 'version.list':
      return listOrEmpty('version.list', listVersions(root))
    case 'instance.list':
      return listOrEmpty('instance.list', listInstances(root))
    case 'instance.create':
      return createInstance(root, {
        name: String(params.name || ''),
        version: String(params.version || ''),
        profile: params.profile ? String(params.profile) : undefined,
      })
    case 'instance.info':
      return getInstance(root, id)
    case 'instance.remove':
      removeInstance(root, id)
      return { ok: true, removed: params.id }
    case 'instance.clone':
      return cloneInstance(root, id, String(params.name || ''))
    case 'instance.rename':
      return renameInstance(root, id, String(params.name || ''))
    case 'instance.pin':
      return pinInstance(root, id, String(params.version || ''))
    case 'instance.display':
      return setInstanceDisplay(root, id, {
        info: params.info != null ? String(params.info) : undefined,
        logo: params.logo === null ? null : params.logo != null ? String(params.logo) : undefined,
        star: params.star != null ? Boolean(params.star) : undefined,
        category: params.category != null ? String(params.category) : undefined,
      })
    case 'import':
      return importPack(root, String(params.path || ''), {
        name: params.name ? String(params.name) : undefined,
        publishedVersions: Array.isArray(params.publishedVersions)
          ? params.publishedVersions.map(String)
          : undefined,
      })
    case 'export':
      return exportInstance(root, id, {
        out: params.out ? String(params.out) : undefined,
      })
    case 'scanDrop':
      return scanDropZips(root, {
        publishedVersions: Array.isArray(params.publishedVersions)
          ? params.publishedVersions.map(String)
          : undefined,
      })
    case 'run':
      return runInstance(root, id, { detach: params.detach !== false })
    case 'stop':
      await stopInstance(root, id)
      return { ok: true, stopped: params.id }
    case 'restart':
      return restartInstance(root, id, { detach: params.detach !== false })
    case 'logs':
      return instanceLogs(root, id)
    case 'ps':
      return readRuntime(root)
    case 'plugin.list':
      return listOrEmpty('plugin.list', pluginList(root, id).bundles)
    case 'plugin.add':
      return pluginAddToInstance(root, id, String(params.spec || ''))
    case 'plugin.remove':
      return pluginRemove(root, id, String(params.pkg || ''))
    case 'plugin.update':
      return pluginUpdate(root, id, String(params.pkg || ''))
    case 'plugin.enable':
      return pluginEnable(root, id, String(params.pkg || ''))
    case 'plugin.disable':
      return pluginDisable(root, id, String(params.pkg || ''))
    case 'pack.list':
      return listOrEmpty('pack.list', await packList(root, id))
    case 'pack.project':
      return packProject(root, id, String(params.path || ''), { allow: Boolean(params.allow) })
    case 'pack.allow':
      await packAllow(root, id, String(params.packId || ''))
      return { ok: true, allowed: params.packId }
    case 'pack.deny':
      await packDeny(root, id, String(params.packId || ''))
      return { ok: true, denied: params.packId }
    case 'pack.setSave':
      await packSetSave(root, id, String(params.name || ''))
      return { ok: true, saved: params.name }
    case 'pack.setLoad':
      await packSetLoad(root, id, String(params.name || ''))
      return { ok: true, loaded: params.name }
    case 'session.list': {
      const r = listSessions(root, id)
      return listOrEmpty('session.list', r.value)
    }
    case 'session.inspect':
      return inspectSession(root, id, String(params.sid || ''))
    case 'session.delete':
      return deleteSession(root, id, String(params.sid || ''))
    case 'session.backup':
      return backupSessions(root, id, params.sid ? String(params.sid) : undefined)
    case 'job.list':
      return listOrEmpty('job.list', listJobs(root))
    case 'job.cancel':
      return cancelJob(root, String(params.jobId || ''))
    case 'crash.analyze':
      return analyzeCrash(root, id)
    case 'doctor':
      return doctorLauncher(root)
    case 'credentials.get':
      return {
        yaml: params.name && String(params.name) !== 'global'
          ? getNamedCredentials(root, String(params.name))
          : getGlobalCredentials(root),
      }
    case 'credentials.set':
      if (params.name && String(params.name) !== 'global') {
        return { path: setNamedCredentials(root, String(params.name), String(params.yaml || '')) }
      }
      return { path: setGlobalCredentials(root, String(params.yaml || '')) }
    case 'credentials.list':
      return listOrEmpty('credentials.list', listCredentialSets(root))
    case 'market.list':
      return listOrEmpty('market.list', marketList(root, { category: params.category ? String(params.category) : undefined }))
    case 'market.search':
      return marketSearch(root, String(params.q || ''))
    case 'market.install':
      return marketInstall(root, id, String(params.name || params.spec || ''))
    case 'update.check':
      return checkUpdate(root, {
        current: String(params.current || '0.0.0'),
        published: Array.isArray(params.published) ? params.published.map(String) : [],
        channel: params.channel === 'dev' ? 'dev' : 'stable',
      })
    case 'update.apply':
      return applyUpdate(root, { version: String(params.version || ''), sourceDir: String(params.sourceDir || '') })
    case 'shortcut.write':
      return { path: writeShortcut(root, id) }
    case 'meta.get':
      return getMeta(root, String(params.key || ''))
    case 'adopt':
      return adoptExistingHome(root, {
        home: params.home ? String(params.home) : undefined,
        name: params.name ? String(params.name) : undefined,
        id: params.id ? String(params.id) : undefined,
        version: String(params.version || ''),
      })
  }
}
