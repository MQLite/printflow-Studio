# 按候选隔离的重建证据

原 `../candidate/` 为已审查的验收包，所有文件保持原字节，仍可按其参数做只读Validate。其README保留了修复前的历史M1警示；当前状态以[M1修复记录](../../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_M1_RESULTS.md)为准。

修复后的 `../checks/build-validate-candidate.ps1 -CandidateId e6-<新的唯一ID>` 写入本目录下对应ID叶目录，并保护全部既有build/manifest/log/run/evidence路径。已失败的ID也不能复用，保留其失败记录并换新ID。构建失败不生成成功summary；Validate失败只写本次失败summary。

`e6-20261022-m1-fixed/` 是M1修复后的一次真实隔离构建与静态Validate证据，不替代原人工验收候选。两个候选的构建源码提交和二进制身份分别记录；不推断新旧DLL逐字一致。

该helper只运行Build、元数据生成、Validate。真人测试仍由用户后续安排；PrepareAndSmoke、Interactive、实际DPI/键鼠/读屏及生产均未执行。
