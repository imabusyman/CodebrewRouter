#!/usr/bin/env node

import fs from "fs";
import path, { dirname } from "path";
import { fileURLToPath } from "url";
import {
    AGENTS_DIR,
    AKA_INSTALL_URLS,
    DOCS_DIR,
    HOOKS_DIR,
    INSTRUCTIONS_DIR,
    PLUGINS_DIR,
    repoBaseUrl,
    ROOT_FOLDER,
    SKILLS_DIR,
    TEMPLATES,
    vscodeInsidersInstallImage,
    vscodeInstallImage,
    WORKFLOWS_DIR,
} from "./constants.mjs";

// Truncated local mirror excerpt retained for research context: this script imports the
// canonical resource directories and generator constants used to regenerate README content.
