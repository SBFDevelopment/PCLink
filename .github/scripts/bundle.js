import path from "node:path";
import fs from "node:fs/promises";
import fsPlain from "node:fs"
import tmp from "tmp-promise";
import { ZipArchive } from 'archiver';
import {spawn} from "promisify-child-process"
import semver from "semver";
import {once} from "node:events"

const [packageRepoPath, manifestRepoPath] = process.argv.slice(2);
if (!packageRepoPath || !manifestRepoPath) {
    throw new Error('Usage: bundle.js <package-repo-path> <manifest-repo-path>');
}

const vccJsonPath = path.join(manifestRepoPath, 'vcc.json');

await assertDirExists(packageRepoPath, "package repo");
await assertDirExists(manifestRepoPath, "vcc manifest repo");

const packageJsonPath = path.join(packageRepoPath, 'package.json');
const packageJson = JSON.parse(await fs.readFile(packageJsonPath, {encoding: 'utf8'}));
const packageName = packageJson.name;

const { path: distDir, cleanup: cleanupDistDir } = await tmp.dir({ unsafeCleanup: true, prefix: 'vcc-release-' });

try {
    console.log(`Processing auto-versioning for ${packageName}...`);

    const allTags = await getTags(packageRepoPath);
    const version = await getNextVersion(allTags);
    const tagName = `v${version}`;

    packageJson.version = version;
    await fs.writeFile(packageJsonPath, JSON.stringify(packageJson, null, 2), 'utf8');

    const zipName = `${packageName}-${version}.zip`;
    const zipPath = path.join(distDir, zipName);

    console.log(`creating package archive: ${zipName}`);
    await createZip(packageRepoPath, zipPath);

    console.log(`Committing version bump to package repository...`);
    await spawn('git', ['config', 'user.name', 'github-actions[bot]'], { cwd: packageRepoPath });
    await spawn('git', ['config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com'], { cwd: packageRepoPath });
    await spawn('git', ['commit', '-am', `Release v${version} [skip ci]`], { cwd: packageRepoPath });
    await spawn('git', ['tag', tagName], { cwd: packageRepoPath });
    await spawn('git', ['push', 'origin', 'release', '--tags'], { cwd: packageRepoPath });

    console.log(`Publishing GitHub release...`);
    await spawn('gh', [
        'release', 'create', tagName, zipPath,
        '-t', `${packageJson.displayName || packageName} v${version}`,
        '-n', `Automated release for version ${version}`,
        '--verify-tag'
    ], { stdio: 'inherit', cwd: packageRepoPath });

    // A clean, user-friendly browser path structure
    const releaseUrl = `https://github.com/${process.env.GITHUB_REPOSITORY}/releases/download/${tagName}/${zipName}`;

    console.log(`Updating central VCC manifest with asset target: ${releaseUrl}`);
    const vccJson = JSON.parse(await fs.readFile(vccJsonPath, 'utf8'));

    if (!vccJson.packages) vccJson.packages = {};
    if (!vccJson.packages[packageName]) {
        vccJson.packages[packageName] = { versions: {} };
    }
    if (vccJson.packages[packageName].versions[version]) {
        throw new Error(`Version ${version} already exists in the central manifest.`);
    }

    const vccPackageEntry = JSON.parse(JSON.stringify(packageJson));
    vccPackageEntry.url = releaseUrl;
    vccJson.packages[packageName].versions[version] = vccPackageEntry;

    vccJson.packages[packageName].versions = Object.fromEntries(
        Object.entries(vccJson.packages[packageName].versions)
            .sort(([v1], [v2]) => semver.compare(v2, v1))
    );

    await fs.writeFile(vccJsonPath, JSON.stringify(vccJson, null, 2));
    console.log("Manifest updated successfully!");

} finally {
    await cleanupDistDir();
}

async function createZip(packageRepoPath, zipPath) {
    const output = fsPlain.createWriteStream(zipPath);
    const archive = new ZipArchive({ zlib: { level: 9 } });

    const archiveError = new Promise((_, reject) => {
        archive.on('error', (err) => reject(err));
    });

    const streamClosed = Promise.race([
        once(output, 'close'),
        once(output, 'error').then(([err]) => Promise.reject(err)),
        archiveError
    ]);
    archive.pipe(output);

    archive.directory(packageRepoPath, false, (entry) => {
        if (entry.name.startsWith('.git') || entry.name.startsWith('node_modules')) return false;
        return entry;
    })

    await archive.finalize();
    await streamClosed;
}

async function assertDirExists(dir, name) {
    let stat;
    try {
        stat = await fs.stat(dir);
    } catch {
        throw new Error(`${name} does not exist: ${dir}`);
    }
    if (!stat.isDirectory()) {
        throw new Error(`${name} is not a directory: ${dir}`);
    }
}

async function getTags(repoPath) {
    const {stdout} = await spawn('git', ['ls-remote', '--tags', 'origin'], {encoding: 'utf8', cwd: repoPath});
    return (stdout || '')
        .split('\n')
        .filter(line => line.includes('refs/tags/'))
        .map(line => line.substring(line.indexOf('refs/tags/') + 10).trim())
        .filter(tag => tag && /^v?\d+\.\d+\.\d+$/.test(tag));
}

async function getNextVersion(allTags) {
    const cleanTags = allTags.map(tag => tag.replace(/^v/, ''));
    if (cleanTags.length === 0) {
        return '1.0.0';
    }
    const maxversion = semver.maxSatisfying(allTags, '*');
    return semver.inc(maxversion, 'minor');
}