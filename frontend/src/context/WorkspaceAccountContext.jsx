import { createContext, useContext } from 'react'

const WorkspaceAccountContext = createContext('local')

export function WorkspaceAccountProvider({ value, children }) {
  return <WorkspaceAccountContext.Provider value={value}>{children}</WorkspaceAccountContext.Provider>
}

export function useWorkspaceAccountKey() {
  return useContext(WorkspaceAccountContext)
}
