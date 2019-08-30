//-----------------------------------------------------------------------------
// FILE:		core.go
// CONTRIBUTOR: John C Burns
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

package logger

import "go.uber.org/zap/zapcore"

type (
	Core struct {
		zapcore.LevelEnabler
		enc zapcore.Encoder
		out zapcore.WriteSyncer
	}
)

func NewCore(enc zapcore.Encoder, ws zapcore.WriteSyncer, enab zapcore.LevelEnabler) zapcore.Core {
	return &Core{
		LevelEnabler: enab,
		enc:          enc,
		out:          ws,
	}
}

func (c Core) With(fields []zapcore.Field) zapcore.Core {
	clone := c.clone()
	for i := range fields {
		fields[i].AddTo(clone.enc)
	}
	return clone
}

func (c Core) Check(entry zapcore.Entry, checkedEntry *zapcore.CheckedEntry) *zapcore.CheckedEntry {
	if c.Enabled(entry.Level) {
		return checkedEntry.AddCore(entry, c)
	}
	return checkedEntry
}

func (c Core) Write(entry zapcore.Entry, fields []zapcore.Field) error {
	buf, err := c.enc.EncodeEntry(entry, fields)
	if err != nil {
		return err
	}
	_, err = c.out.Write(buf.Bytes())
	buf.Free()
	if err != nil {
		return err
	}
	if entry.Level > zapcore.ErrorLevel {
		c.Sync()
	}
	return nil
}

func (c Core) Sync() error {
	return c.out.Sync()
}

func (c Core) clone() *Core {
	return &Core{
		LevelEnabler: c.LevelEnabler,
		enc:          c.enc,
		out:          c.out,
	}
}
