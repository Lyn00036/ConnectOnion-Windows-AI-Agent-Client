export type RepositoryResult<T> = Promise<T>

// TODO(repository-contracts): Use this as the common shape for renderer-facing
// repositories, but keep implementation details in Electron main. React feature
// modules should call typed repository adapters, not SQL or ipcRenderer directly.

export type LocalRepository<TEntity, TCreateInput, TUpdateInput> = {
  list: () => RepositoryResult<TEntity[]>
  get: (id: string) => RepositoryResult<TEntity | null>
  create: (input: TCreateInput) => RepositoryResult<TEntity>
  update: (id: string, input: TUpdateInput) => RepositoryResult<TEntity>
  remove: (id: string) => RepositoryResult<void>
}
