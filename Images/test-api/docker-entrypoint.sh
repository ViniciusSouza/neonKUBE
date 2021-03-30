#!/bin/bash
#------------------------------------------------------------------------------
# FILE:         docker-entrypoint.sh
# CONTRIBUTOR:  Marcus Bowyer
# COPYRIGHT:    Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.

# Add the root directory to the PATH.

PATH=${PATH}:/

# container init
. ./container-init.sh

# Launch the service.

exec "test-api""
